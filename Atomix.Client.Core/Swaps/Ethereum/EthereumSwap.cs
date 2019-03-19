using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.Ethereum;
using Atomix.Common;
using Atomix.Common.Abstract;
using Atomix.Core.Entities;
using Atomix.Swaps.Abstract;
using Atomix.Wallet.Abstract;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Nethereum.Web3;
using Nethereum.Contracts.Extensions;

namespace Atomix.Swaps.Ethereum
{
    public class EthereumSwap : Swap
    {
        public EthereumSwap(
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

        private async Task<IEnumerable<EthereumTransaction>> CreatePaymentTxsAsync(
            int lockTimeInHours,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Log.Debug(
                messageTemplate: "Create payment transactions for swap {@swap}",
                propertyValue: _swapState.Id);

            var order = _swapState.Order;
            var requiredAmountInEth = AmountHelper.QtyToAmount(order.Side, order.LastQty, order.LastPrice);
            var refundTimeInSeconds = (uint) lockTimeInHours * 60 * 60;
            var master = true;

            var transactions = new List<EthereumTransaction>();

            foreach (var walletAddress in order.FromWallets)
            {
                Log.Debug("Create swap payment tx from address {@address}", walletAddress.Address);

                var balanceInEth = await _account
                    .GetBalanceAsync(Currencies.Eth, walletAddress.Address)
                    .ConfigureAwait(false);

                Log.Debug("Available balance: {@balance}", balanceInEth);

                var feeAmountInEth = Atomix.Ethereum.GetDefaultPaymentFeeAmount();
                var amountInEth = Math.Min(balanceInEth - feeAmountInEth, requiredAmountInEth);
                requiredAmountInEth -= amountInEth;

                // todo: offline nonce using
                var nonce = await ((IEthereumBlockchainApi)Currencies.Eth.BlockchainApi)
                    .GetTransactionCountAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var message = new InitiateFunctionMessage()
                {
                    HashedSecret = _swapState.SecretHash,
                    RefundTime = refundTimeInSeconds,
                    Participant = _swapState.Requisites.ToWallet.Address,
                    AmountToSend = Atomix.Ethereum.EthToWei(amountInEth),
                    FromAddress = walletAddress.Address,
                    GasPrice = Atomix.Ethereum.GweiToWei(Atomix.Ethereum.DefaultGasPriceInGwei),
                    Gas = Atomix.Ethereum.DefaultPaymentTxGasLimit,
                    Nonce = nonce,
                    Master = master
                };

                var txInput = message.CreateTransactionInput(Currencies.Eth.SwapContractAddress);

                transactions.Add(new EthereumTransaction(txInput) {
                    Type = EthereumTransaction.OutputTransaction
                });

                if (master)
                    master = !master;

                if (requiredAmountInEth == 0)
                    break;
            }

            return transactions;
        }

        private async Task<bool> SignPaymentTxsAsync(
            IEnumerable<EthereumTransaction> transactions,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            foreach (var transaction in transactions)
            {
                var signResult = await SignTransactionAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult) {
                    Log.Error("Transaction signing error");
                    return false;
                }
            }

            return true;
        }

        private async Task<bool> SignTransactionAsync(
            EthereumTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _account.Wallet
                .SignAsync(transaction, transaction.From, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task BroadcastPaymentTxsAsync(
            IEnumerable<EthereumTransaction> transactions,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // broadcast payment transactions
            foreach (var transaction in transactions)
                await BroadcastTxAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);
        }

        private async Task BroadcastTxAsync(
            EthereumTransaction tx,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var txId = await Currencies.Eth.BlockchainApi
                .BroadcastAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            Log.Debug(
                messageTemplate: "Payment txId {@id}",
                propertyValue: txId);

            // account new unconfirmed transaction
            await _account
                .AddUnconfirmedTransactionAsync(
                    tx: tx,
                    selfAddresses: new[] {tx.From},
                    notify: true)
                .ConfigureAwait(false);

            // todo: transaction receipt status control
        }

        private async Task CreateAndBroadcastPaymentTxAsync(
            int lockTimeHours,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var txs = await CreatePaymentTxsAsync(lockTimeHours, cancellationToken)
                .ConfigureAwait(false);

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
            _taskPerformer.EnqueueTask(new EthereumRedeemControlTask
            {
                Currency = _currency,
                RefundTime = DateTime.UtcNow.AddHours(lockTimeHours), // todo: not fully correct, use instead DTO.initialtimestamp + refundtimeinterval
                SwapState = _swapState,
                From = txs.First().From,
                CompleteHandler = RedeemControlCompletedEventHandler,
                CancelHandler = RedeemControlCanceledEventHandler
            });
        }

        private void RedeemControlCompletedEventHandler(
            BackgroundTask task)
        {
            if (!(task is EthereumRedeemControlTask redeemControlTask)) {
                Log.Error("Incorrect background task type.");
                return;
            }

            var swap = redeemControlTask?.SwapState;

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
            if (!(task is EthereumRedeemControlTask redeemControlTask)) {
                Log.Error("Incorrect background task type.");
                return;
            }

            var swap = redeemControlTask?.SwapState;

            Log.Debug(
                messageTemplate: "Handle redeem control canceled event for swap {@swapId}",
                propertyValue: swap.Id);

            _taskPerformer.EnqueueTask(new EthereumRefundTimeControlTask
            {
                Currency = _currency,
                RefundTimeUtc = redeemControlTask.RefundTime,
                SwapState = swap,
                From = redeemControlTask.From,
                CompleteHandler = RefundTimeReachedEventHandler
            });
        }

        private async void RefundTimeReachedEventHandler(
            BackgroundTask task)
        {
            if (!(task is EthereumRefundTimeControlTask refundTask))
            {
                Log.Error("Incorrect background task type.");
                return;
            }

            try
            {
                await RefundAsync(
                        refundTime: refundTask.RefundTimeUtc,
                        masterAddress: refundTask.From)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Refund error");
            }
        }

        private async Task RefundAsync(
            DateTime refundTime,
            string masterAddress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Log.Debug("Create refund for address {@address} and swap {@swap}", masterAddress, _swapState.Id);

            var web3 = new Web3(Web3BlockchainApi.UriByChain(Currencies.Eth.Chain));
            var txHandler = web3.Eth.GetContractTransactionHandler<RefundFunctionMessage>();

            var nonce = await ((IEthereumBlockchainApi)Currencies.Eth.BlockchainApi)
                .GetTransactionCountAsync(masterAddress, cancellationToken)
                .ConfigureAwait(false);

            var message = new RefundFunctionMessage() {
                FromAddress = masterAddress,
                HashedSecret = _swapState.SecretHash,
                GasPrice = Atomix.Ethereum.GweiToWei(Atomix.Ethereum.DefaultGasPriceInGwei),
                Gas = Atomix.Ethereum.DefaultRefundTxGasLimit,
                Nonce = nonce
            };

            var txInput = message.CreateTransactionInput(Currencies.Eth.SwapContractAddress);

            var refundTx = new EthereumTransaction(txInput) {
                Type = EthereumTransaction.OutputTransaction
            };

            var signResult = await SignTransactionAsync(refundTx, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult) {
                Log.Error("Transaction signing error");
                return;
            }

            await BroadcastTxAsync(refundTx)
                .ConfigureAwait(false);

            _swapState.RefundTx = refundTx;
            _swapState.SetRefundSigned();
            _swapState.SetRefundBroadcast();
            _swapState.SetRefundConfirmed(); // todo: check transaction receipt status before
        }

        public override Task AcceptSwapAsync()
        {
            Log.Debug(
                messageTemplate: "Accept swap {@swapId}",
                propertyValue: _swapState.Id);

            // wait initiator payment for counter party purchased currency
            if (_swapState.Order.PurchasedCurrency().Name.Equals(Currencies.Eth.Name))
            {
                _taskPerformer.EnqueueTask(new EthereumSwapInitiatedControlTask
                {
                    Currency = _currency,
                    SwapState = _swapState,
                    Interval = TimeSpan.FromSeconds(20),
                    CompleteHandler = SwapInitiatedEventHandler
                });
            }

            return Task.CompletedTask;
        }

        private void SwapInitiatedEventHandler(BackgroundTask task)
        {
            Log.Debug("Initiator payment transaction received. Now counter party can broadcast payment tx");

            _swapState.PartyPaymentTx = null; // todo: change to set party payment flag
            _swapState.SetPartyPaymentConfirmed(); // todo: more flags?

            InitiatorPaymentConfirmed?.Invoke(this, new SwapEventArgs(_swapState));
        }

        public override async Task RestoreSwapAsync()
        {
            if (_swapState.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast)) {
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

        private async Task TryRefundAsync(
            IBlockchainTransaction paymentTx,
            string paymentTxId,
            DateTime refundTime)
        {
            if (!(paymentTx is EthereumTransaction ethTx))
            {
                if (paymentTxId == null) {
                    Log.Error("Can't found payment transaction to recover refund address");
                    return;
                }

                var tx = await Currencies.Eth.BlockchainApi
                    .GetTransactionAsync(paymentTxId)
                    .ConfigureAwait(false);

                if (tx is EthereumTransaction) {
                    ethTx = (EthereumTransaction)tx;
                } else {
                    Log.Error("Can't found payment transaction to recover refund address");
                    return;
                }
            }

            _taskPerformer.EnqueueTask(new EthereumRefundTimeControlTask
            {
                Currency = _currency,
                RefundTimeUtc = refundTime,
                SwapState = _swapState,
                From = ethTx.From,
                CompleteHandler = RefundTimeReachedEventHandler
            });
        }     

        public override async Task RedeemAsync()
        {
            Log.Debug("Create redeem for swap {@swap}", _swapState.Id);

            var web3 = new Web3(Web3BlockchainApi.UriByChain(Currencies.Eth.Chain));
            var txHandler = web3.Eth.GetContractTransactionHandler<RedeemFunctionMessage>();

            var nonce = await ((IEthereumBlockchainApi)Currencies.Eth.BlockchainApi)
                .GetTransactionCountAsync(_swapState.Order.ToWallet.Address)
                .ConfigureAwait(false);

            var message = new RedeemFunctionMessage() {
                FromAddress = _swapState.Order.ToWallet.Address,
                HashedSecret = _swapState.SecretHash,
                Secret = _swapState.Secret,
                Nonce = nonce,
                GasPrice = Atomix.Ethereum.GweiToWei(Atomix.Ethereum.DefaultGasPriceInGwei),
                Gas = Atomix.Ethereum.DefaultRedeemTxGasLimit
            };

            var txInput = message.CreateTransactionInput(Currencies.Eth.SwapContractAddress);

            var redeemTx = new EthereumTransaction(txInput) {
                Type = EthereumTransaction.OutputTransaction
            };

            var signResult = await SignTransactionAsync(redeemTx)
                .ConfigureAwait(false);

            if (!signResult) {
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
    }
}