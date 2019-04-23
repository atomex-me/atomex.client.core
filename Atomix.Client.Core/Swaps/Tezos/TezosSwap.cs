using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.Tezos;
using Atomix.Common;
using Atomix.Common.Abstract;
using Atomix.Core.Entities;
using Atomix.Swaps.Abstract;
using Atomix.Wallet.Abstract;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomix.Swaps.Tezos
{
    public class TezosSwap : CurrencySwap
    {
        public TezosSwap(
            Currency currency,
            SwapState swapState,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer)
            : base(
                currency,
                swapState,
                account,
                swapClient,
                taskPerformer)
        {
        }

        public override async Task InitiateSwapAsync()
        {
            Log.Debug(
                messageTemplate: "Initiate swap {@swapId}",
                propertyValue: _swapState.Id);

            CreateSecret();
            CreateSecretHash();

            SendData(SwapDataType.SecretHash, _swapState.SecretHash);

            await CreateAndBroadcastPaymentTxAsync(DefaultInitiatorLockTimeHours)
                .ConfigureAwait(false);
        }

        public override Task AcceptSwapAsync()
        {
            Log.Debug(
                messageTemplate: "Accept swap {@swapId}",
                propertyValue: _swapState.Id);

            return Task.CompletedTask;
        }

        public override Task PrepareToReceiveAsync()
        {
            // initiator waits "accepted" event, counterParty waits "initiated" event
            var handler = _swapState.IsInitiator
                ? (OnTaskDelegate)SwapAcceptedEventHandler
                : (OnTaskDelegate)SwapInitiatedEventHandler;

            var refundTime = _swapState.IsInitiator
                ? DefaultCounterPartyLockTimeHours * 60 * 60
                : DefaultInitiatorLockTimeHours * 60 * 60;

            _taskPerformer.EnqueueTask(new TezosSwapInitiatedControlTask
            {
                Currency = _currency,
                SwapState = _swapState,
                Interval = TimeSpan.FromSeconds(30),
                RefundTime = refundTime,
                CompleteHandler = handler
            });

            return Task.CompletedTask;
        }

        public override async Task RestoreSwapAsync()
        {
            if (_swapState.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast))
            {
                // todo: check confirmation
                return;
            }

            if (_swapState.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                var lockTimeHours = _swapState.IsInitiator
                    ? DefaultInitiatorLockTimeHours
                    : DefaultCounterPartyLockTimeHours;

                await TryRefundAsync(
                        paymentTx: _swapState.PaymentTx,
                        paymentTxId: _swapState.PaymentTxId,
                        refundTime: _swapState.Order.TimeStamp.AddHours(lockTimeHours))
                    .ConfigureAwait(false);
            }
        }

        public override Task HandleSwapData(SwapData swapData)
        {
            throw new Exception("Invalid swap data type");
        }

        public override async Task RedeemAsync()
        {
            Log.Debug("Create redeem for swap {@swapId}", _swapState.Id);

            var redeemTx = new TezosTransaction
            {
                From = _swapState.Order.ToWallet.Address,
                To = Currencies.Xtz.SwapContractAddress,
                Amount = 0,
                Fee = Atomix.Tezos.DefaultRedeemFee,
                GasLimit = Atomix.Tezos.DefaultRedeemGasLimit,
                StorageLimit = Atomix.Tezos.DefaultRedeemStorageLimit,
                Params = RedeemParams(),
                Type = TezosTransaction.OutputTransaction
            };

            var signResult = await SignTransactionAsync(redeemTx)
                .ConfigureAwait(false);

            if (!signResult)
            {
                Log.Error("Transaction signing error");
                return;
            }

            await BroadcastTxAsync(redeemTx)
                .ConfigureAwait(false);

            _swapState.RedeemTx = redeemTx;
            _swapState.SetRedeemBroadcast();
            _swapState.SetRedeemSigned();
        }

        public override Task BroadcastPaymentAsync()
        {
            var lockTimeHours = _swapState.IsInitiator
                ? DefaultInitiatorLockTimeHours
                : DefaultCounterPartyLockTimeHours;

            return CreateAndBroadcastPaymentTxAsync(lockTimeHours);
        }      

        private async Task TryRefundAsync(
            IBlockchainTransaction paymentTx,
            string paymentTxId,
            DateTime refundTime)
        {
            if (!(paymentTx is TezosTransaction xtzTx))
            {
                if (paymentTxId == null)
                {
                    Log.Error("Can't found payment transaction to recover refund address");
                    return;
                }

                var tx = await Currencies.Xtz.BlockchainApi
                    .GetTransactionAsync(paymentTxId)
                    .ConfigureAwait(false);

                if (tx is TezosTransaction transaction)
                {
                    xtzTx = transaction;
                }
                else
                {
                    Log.Error("Can't found payment transaction to recover refund address");
                    return;
                }
            }

            _taskPerformer.EnqueueTask(new TezosRefundTimeControlTask
            {
                Currency = _currency,
                RefundTimeUtc = refundTime,
                SwapState = _swapState,
                From = xtzTx.From,
                CompleteHandler = RefundTimeReachedEventHandler
            });
        }

        private void SwapInitiatedEventHandler(BackgroundTask task)
        {
            Log.Debug(
                "Initiator payment transaction received. Now counter party can broadcast payment tx for swap {@swapId}",
                _swapState.Id);

            _swapState.PartyPaymentTx = null; // todo: change to set party payment flag
            _swapState.SetPartyPaymentConfirmed(); // todo: more flags?

            InitiatorPaymentConfirmed?.Invoke(this, new SwapEventArgs(_swapState));
        }

        private async void SwapAcceptedEventHandler(BackgroundTask task)
        {
            try
            {
                Log.Debug(
                    "CounterParty payment transaction received. Now counter party can redeem for swap {@swapId}",
                    _swapState.Id);

                _swapState.PartyPaymentTx = null; // todo: change to set party payment flag
                _swapState.SetPartyPaymentConfirmed(); // todo: more flags?

                await RedeemAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap accepted error");
            }
        }

        private async Task<IEnumerable<TezosTransaction>> CreatePaymentTxsAsync(
            int lockTimeInHours,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Log.Debug(
                messageTemplate: "Create payment transactions for swap {@swapId}",
                propertyValue: _swapState.Id);

            var order = _swapState.Order;
            var requiredAmountInMtz = AmountHelper
                .QtyToAmount(order.Side, order.LastQty, order.LastPrice)
                .ToMicroTez();

            var refundTimeInSeconds = (uint)lockTimeInHours * 60 * 60;
            var isMasterAddress = true;

            var transactions = new List<TezosTransaction>();

            foreach (var walletAddress in order.FromWallets)
            {
                Log.Debug(
                    "Create swap payment tx from address {@address} for swap {@swapId}",
                    walletAddress.Address,
                    _swapState.Id);

                var balanceInTz = await _account
                    .GetBalanceAsync(
                        currency: Currencies.Xtz,
                        address: walletAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                Log.Debug("Available balance: {@balance}", balanceInTz);

                var balanceInMtz = balanceInTz.ToMicroTez();

                var feeAmountInMtz = Atomix.Tezos.DefaultPaymentFee;

                // reserve funds for refund from master address
                var reservedAmountInMtz = isMasterAddress
                    ? Atomix.Tezos.DefaultRefundFee // todo: + reserved amount for address
                    : 0;

                var amountInMtz = Math.Min(balanceInMtz - feeAmountInMtz - reservedAmountInMtz, requiredAmountInMtz);

                if (amountInMtz <= 0)
                {
                    Log.Warning(
                        "Insufficient funds at {@address}. Balance: {@balance}, " +
                        "feeAmount: {@feeAmount}, reserved: {@reserved}, result: {@result}.",
                        walletAddress.Address,
                        balanceInMtz,
                        feeAmountInMtz,
                        reservedAmountInMtz,
                        amountInMtz);
                    continue;
                }

                requiredAmountInMtz -= amountInMtz;

                transactions.Add(new TezosTransaction
                {
                    From = walletAddress.Address,
                    To = Currencies.Xtz.SwapContractAddress,
                    Amount = Math.Round(amountInMtz, 0),
                    Fee = feeAmountInMtz,
                    GasLimit = Atomix.Tezos.DefaultPaymentGasLimit,
                    StorageLimit = Atomix.Tezos.DefaultPaymentStorageLimit,
                    Params = InitParams(refundTimeInSeconds, isMasterAddress),
                    Type = TezosTransaction.OutputTransaction
                });

                if (isMasterAddress)
                    isMasterAddress = false;

                if (requiredAmountInMtz == 0)
                    break;
            }

            return transactions;
        }

        private async Task<bool> SignPaymentTxsAsync(
            IEnumerable<TezosTransaction> transactions,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            foreach (var transaction in transactions)
            {
                var signResult = await SignTransactionAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                {
                    Log.Error("Transaction signing error");
                    return false;
                }
            }

            return true;
        }

        private async Task<bool> SignTransactionAsync(
            TezosTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _account.Wallet
                .SignAsync(transaction, transaction.From, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task BroadcastPaymentTxsAsync(
            IEnumerable<TezosTransaction> transactions,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // broadcast payment transactions
            foreach (var transaction in transactions)
                await BroadcastTxAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);
        }

        private async Task BroadcastTxAsync(
            TezosTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var txId = await Currencies.Xtz.BlockchainApi
                .BroadcastAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug(
                messageTemplate: "Payment txId {@id} for swap {@swapId}",
                propertyValue0: txId,
                propertyValue1: _swapState.Id);

            // account new unconfirmed transaction
            await _account
                .AddUnconfirmedTransactionAsync(
                    tx: tx,
                    selfAddresses: new[] { tx.From },
                    notify: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // todo: transaction receipt status control
        }

        private async Task CreateAndBroadcastPaymentTxAsync(
            int lockTimeHours,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var txs = (await CreatePaymentTxsAsync(lockTimeHours, cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            var signResult = await SignPaymentTxsAsync(txs, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return;

            await BroadcastPaymentTxsAsync(txs, cancellationToken)
                .ConfigureAwait(false);

            _swapState.PaymentTx = txs.First();
            _swapState.PaymentTxId = txs.First().Id;
            _swapState.SetPaymentBroadcast();

            // start redeem control
            _taskPerformer.EnqueueTask(new TezosRedeemControlTask
            {
                Currency = _currency,
                RefundTimeUtc = DateTime.UtcNow.AddHours(lockTimeHours), // todo: not fully correct, use instead DTO.initialtimestamp + refundtimeinterval
                SwapState = _swapState,
                From = txs.First().From,
                CompleteHandler = RedeemControlCompletedEventHandler,
                CancelHandler = RedeemControlCanceledEventHandler
            });
        }

        private void RedeemControlCompletedEventHandler(
            BackgroundTask task)
        {
            if (!(task is TezosRedeemControlTask redeemControlTask))
            {
                Log.Error("Incorrect background task type.");
                return;
            }

            var swap = redeemControlTask.SwapState;

            Log.Debug(
                messageTemplate: "Handle redeem control completed event for swap {@swapId}",
                propertyValue: swap.Id);

            if (swap.IsCounterParty)
            {
                swap.Secret = redeemControlTask.Secret;

                CounterPartyPaymentSpent?.Invoke(this, new SwapEventArgs(swap));
            }
        }

        private void RedeemControlCanceledEventHandler(
            BackgroundTask task)
        {
            if (!(task is TezosRedeemControlTask redeemControlTask))
            {
                Log.Error("Incorrect background task type.");
                return;
            }

            var swap = redeemControlTask.SwapState;

            Log.Debug(
                messageTemplate: "Handle redeem control canceled event for swap {@swapId}",
                propertyValue: swap.Id);

            _taskPerformer.EnqueueTask(new TezosRefundTimeControlTask
            {
                Currency = _currency,
                RefundTimeUtc = redeemControlTask.RefundTimeUtc,
                SwapState = swap,
                From = redeemControlTask.From,
                CompleteHandler = RefundTimeReachedEventHandler
            });
        }

        private async void RefundTimeReachedEventHandler(
            BackgroundTask task)
        {
            if (!(task is TezosRefundTimeControlTask refundTask))
            {
                Log.Error("Incorrect background task type.");
                return;
            }

            try
            {
                await RefundAsync(
                        masterAddress: refundTask.From)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Refund error");
            }
        }

        private async Task RefundAsync(
            string masterAddress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Log.Debug(
                "Create refund for address {@address} and swap {@swap}",
                masterAddress,
                _swapState.Id);

            var refundTx = new TezosTransaction
            {
                From = masterAddress,
                To = Currencies.Xtz.SwapContractAddress,
                Fee = Atomix.Tezos.DefaultRefundFee,
                GasLimit = Atomix.Tezos.DefaultRefundGasLimit,
                StorageLimit = Atomix.Tezos.DefaultRefundStorageLimit,
                Params = RefundParams(),
                Type = TezosTransaction.OutputTransaction
            };

            var signResult = await SignTransactionAsync(refundTx, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
            {
                Log.Error("Transaction signing error");
                return;
            }

            await BroadcastTxAsync(refundTx, cancellationToken)
                .ConfigureAwait(false);

            _swapState.RefundTx = refundTx;
            _swapState.SetRefundSigned();
            _swapState.SetRefundBroadcast();
            _swapState.SetRefundConfirmed(); // todo: check transaction receipt status before
        }

        private JObject InitParams(long refundTime, bool isMaster)
        {
            return JObject.Parse(@"{'prim':'Left','args':[{'prim':'Pair','args':[{'int':'" + refundTime + "'},{'prim':'Pair','args':[{'bytes':'" + _swapState.SecretHash.ToHexString() + "'},{'prim':'Pair','args':[{'string':'" + _swapState.Requisites.ToWallet.Address + "'},{'prim':'" + isMaster + "'}]}]}]}]}");
        }

        private JObject RedeemParams()
        {
            return JObject.Parse(
                @"{'prim':'Right','args':[{'prim':'Left','args':[{'prim':'Pair','args':[{'bytes':'" + _swapState.SecretHash.ToHexString() + "'},{'bytes':'" + _swapState.Secret.ToHexString() + "'}]}]}]}");
        }

        private JObject RefundParams()
        {
            return JObject.Parse(
                @"{'prim':'Right','args':[{'prim':'Right','args':[{'bytes':'" + _swapState.SecretHash.ToHexString() + "'}]}]}");
        }
    }
}