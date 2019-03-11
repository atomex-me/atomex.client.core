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
    public class EthereumSwapProtocol : SwapProtocol
    {
        public EthereumSwapProtocol(
            Currency currency,
            Swap swap,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer,
            OnSwapUpdatedDelegate onSwapUpdated = null)
            : base(
                currency,
                swap,
                account,
                swapClient,
                taskPerformer,
                onSwapUpdated)
        {
        }

        public override async Task InitiateSwapAsync()
        {
            Log.Debug(
                messageTemplate: "Initiate swap {@swapId}",
                propertyValue: _swap.Id);

            CreateSecret();
            CreateSecretHash();

            SendData(SwapDataType.SecretHash, _swap.SecretHash);

            await CreateAndBroadcastInitiatorPaymentTxAsync()
                .ConfigureAwait(false);
        }

        private async Task<IEnumerable<EthereumTransaction>> CreatePaymentTxsAsync(
            int lockTimeInHours,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Log.Debug(
                messageTemplate: "Create payment transactions for swap {@swap}",
                propertyValue: _swap.Id);

            var order = _swap.Order;
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
                    HashedSecret = _swap.SecretHash,
                    RefundTime = refundTimeInSeconds,
                    Participant = _swap.Requisites.ToWallet.Address,
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

        private async Task CreateAndBroadcastInitiatorPaymentTxAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var txs = await CreatePaymentTxsAsync(DefaultInitiatorLockTimeHours, cancellationToken)
                .ConfigureAwait(false);

            var signResult = await SignPaymentTxsAsync(txs, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return;

            await BroadcastPaymentTxsAsync(txs, cancellationToken)
                .ConfigureAwait(false);

            _swap.SetInitiatorPaymentTx(txs.First());
            _swap.SetInitiatorPaymentSignedTx(txs.First());
            _swap.SetInitiatorPaymentBroadcast();
            _swap.SetInitiatorPaymentTxId(txs.First().Id);
            RaiseSwapUpdated(_swap);
    
            // start redeem control
            _taskPerformer.EnqueueTask(new EthereumRedeemControlTask
            {
                Currency = _currency,
                RefundTime = DateTime.UtcNow.AddHours(DefaultInitiatorLockTimeHours), // todo: not fully correct, use instead DTO.initialtimestamp + refundtimeinterval
                Swap = _swap,
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

            var swap = redeemControlTask?.Swap;

            Log.Debug(
                messageTemplate: "Handle redeem control completed event for swap {@swapId}",
                propertyValue: swap.Id);

            if (swap.IsCounterParty)
            {
                swap.SetSecret(redeemControlTask.Secret);
                RaiseSwapUpdated(swap);

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

            var swap = redeemControlTask?.Swap;

            Log.Debug(
                messageTemplate: "Handle redeem control canceled event for swap {@swapId}",
                propertyValue: swap.Id);

            _taskPerformer.EnqueueTask(new EthereumRefundTimeControlTask
            {
                Currency = _currency,
                RefundTimeUtc = redeemControlTask.RefundTime,
                Swap = swap,
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

        private async Task CreateAndBroadcastCounterPartyPaymentTxAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var txs = await CreatePaymentTxsAsync(DefaultCounterPartyLockTimeHours, cancellationToken)
                .ConfigureAwait(false);

            var signResult = await SignPaymentTxsAsync(txs, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
                return;

            await BroadcastPaymentTxsAsync(txs, cancellationToken)
                .ConfigureAwait(false);

            _swap.SetCounterPartyPaymentTx(txs.First());
            _swap.SetCounterPartyPaymentSignedTx(txs.First());
            _swap.SetCounterPartyPaymentBroadcast();
            _swap.SetCounterPartyPaymentTxId(txs.First().Id);
            RaiseSwapUpdated(_swap);

            // start redeem control
            _taskPerformer.EnqueueTask(new EthereumRedeemControlTask
            {
                Currency = _currency,
                RefundTime = DateTime.UtcNow.AddHours(DefaultCounterPartyLockTimeHours), // todo: not fully correct, use instead DTO.initialtimestamp + refundtimeinterval
                Swap = _swap,
                From = txs.First().From,
                CompleteHandler = RedeemControlCompletedEventHandler,
                CancelHandler = RedeemControlCanceledEventHandler
            });
        }

        private async Task RefundAsync(
            DateTime refundTime,
            string masterAddress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Log.Debug("Create refund for address {@address} and swap {@swap}", masterAddress, _swap.Id);

            var web3 = new Web3(Web3BlockchainApi.UriByChain(Currencies.Eth.Chain));
            var txHandler = web3.Eth.GetContractTransactionHandler<RefundFunctionMessage>();

            var nonce = await ((IEthereumBlockchainApi)Currencies.Eth.BlockchainApi)
                .GetTransactionCountAsync(masterAddress, cancellationToken)
                .ConfigureAwait(false);

            var message = new RefundFunctionMessage() {
                FromAddress = masterAddress,
                HashedSecret = _swap.SecretHash,
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

            if (_swap.IsInitiator) {
                _swap.SetInitiatorRefundTx(refundTx);
                _swap.SetInitiatorRefundSignedTx(refundTx);
                _swap.SetInitiatorRefundBroadcast();
                _swap.SetInitiatorRefundConfirmed();
            } else {
                _swap.SetCounterPartyRefundTx(refundTx);
                _swap.SetCounterPartyRefundSignedTx(refundTx);
                _swap.SetCounterPartyRefundBroadcast();
                _swap.SetCounterPartyRefundConfirmed();
            }

            RaiseSwapUpdated(_swap);
        }

        public override Task AcceptSwapAsync()
        {
            Log.Debug(
                messageTemplate: "Accept swap {@swapId}",
                propertyValue: _swap.Id);

            // wait initiator payment for counter party purchased currency
            if (_swap.Order.PurchasedCurrency().Name.Equals(Currencies.Eth.Name))
            {
                _taskPerformer.EnqueueTask(new EthereumSwapInitiatedControlTask
                {
                    Currency = _currency,
                    Swap = _swap,
                    Interval = TimeSpan.FromSeconds(20),
                    CompleteHandler = SwapInitiatedEventHandler
                });
            }

            return Task.CompletedTask;
        }

        private void SwapInitiatedEventHandler(BackgroundTask task)
        {
            Log.Debug("Initiator payment transaction received. Now counter party can broadcast payment tx");

            _swap.SetInitiatorPaymentSignedTx(null);
            _swap.SetInitiatorPaymentConfirmed();
            RaiseSwapUpdated(_swap);

            InitiatorPaymentConfirmed?.Invoke(this, new SwapEventArgs(_swap));
        }

        public override async Task RestoreSwapAsync()
        {
            if (_swap.IsInitiator)
            {
                if (_swap.State.HasFlag(SwapState.IsInitiatorRefundBroadcast)) {
                    // todo: check confirmation
                    return;
                }

                if (_swap.State.HasFlag(SwapState.IsInitiatorPaymentBroadcast))
                {
                    await TryRefundAsync(
                            paymentTx: _swap.InitiatorPaymentSignedTx,
                            paymentTxId: _swap.InitiatorPaymentTxId,
                            refundTime: _swap.Order.TimeStamp.AddHours(DefaultInitiatorLockTimeHours))
                        .ConfigureAwait(false);
                }
            }
            else
            {
                if (_swap.State.HasFlag(SwapState.IsCounterPartyRefundBroadcast)) {
                    // todo: check confirmation
                    return;
                }

                if (_swap.State.HasFlag(SwapState.IsCounterPartyPaymentBroadcast))
                {
                    await TryRefundAsync(
                            paymentTx: _swap.CounterPartyPaymentSignedTx,
                            paymentTxId: _swap.CounterPartyPaymentTxId,
                            refundTime: _swap.Order.TimeStamp.AddHours(DefaultCounterPartyLockTimeHours))
                        .ConfigureAwait(false);
                }
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
                Swap = _swap,
                From = ethTx.From,
                CompleteHandler = RefundTimeReachedEventHandler
            });
        }     

        public override async Task RedeemAsync()
        {
            Log.Debug("Create redeem for swap {@swap}", _swap.Id);

            var web3 = new Web3(Web3BlockchainApi.UriByChain(Currencies.Eth.Chain));
            var txHandler = web3.Eth.GetContractTransactionHandler<RedeemFunctionMessage>();

            var nonce = await ((IEthereumBlockchainApi)Currencies.Eth.BlockchainApi)
                .GetTransactionCountAsync(_swap.Order.ToWallet.Address)
                .ConfigureAwait(false);

            var message = new RedeemFunctionMessage() {
                FromAddress = _swap.Order.ToWallet.Address,
                HashedSecret = _swap.SecretHash,
                Secret = _swap.Secret,
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

            if (_swap.IsInitiator) {
                _swap.SetCounterPartyRedeemBroadcast();
                _swap.SetCounterPartyRedeemTx(redeemTx);
                _swap.SetCounterPartyRedeemSignedTx(redeemTx);
            } else {
                _swap.SetInitiatorRedeemBroadcast();
                _swap.SetInitiatorRedeemTx(redeemTx);
                _swap.SetInitiatorRedeemSignedTx(redeemTx);
            }

            RaiseSwapUpdated(_swap);
        }

        public override Task BroadcastPayment()
        {
            if (_swap.IsInitiator) {
                return CreateAndBroadcastInitiatorPaymentTxAsync();
            } else {
                return CreateAndBroadcastCounterPartyPaymentTxAsync();
            }
        }
    }
}