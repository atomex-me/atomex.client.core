using System;
using System.Linq;
using System.Threading.Tasks;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Common;
using Atomix.Common.Abstract;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Swaps.Abstract;
using Atomix.Swaps.Tasks;
using Atomix.Wallet;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Swaps.BitcoinBased
{
    public class BitcoinBasedSwap : CurrencySwap
    {
        private readonly IBitcoinBasedSwapTransactionFactory _transactionFactory;

        public BitcoinBasedSwap(
            Currency currency,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer,
            IBitcoinBasedSwapTransactionFactory transactionFactory)
            : base(
                currency,
                account,
                swapClient,
                taskPerformer)
        {
            _transactionFactory = transactionFactory ??
                throw new ArgumentNullException(nameof(transactionFactory));
        }

        public override async Task BroadcastPaymentAsync(ClientSwap swap)
        {
            if (swap.IsAcceptor &&
                (!swap.StateFlags.HasFlag(SwapStateFlags.HasPartyPayment) ||
                 !swap.StateFlags.HasFlag(SwapStateFlags.IsPartyPaymentConfirmed)))
            {
                Log.Debug("CounterParty is not ready to broadcast payment tx for swap {@swap}", swap.Id);

                return;
            }

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            await CreatePaymentAsync(swap, lockTimeInSeconds)
                .ConfigureAwait(false);

            Log.Debug("Broadcast payment tx for swap {@swap}", swap.Id);

            var currency = swap.SoldCurrency;

            // broadcast payment transaction
            var txId = await currency.BlockchainApi
                .BroadcastAsync(swap.PaymentTx)
                .ConfigureAwait(false);

            swap.PaymentTxId = txId ?? throw new Exception("Transaction Id is null");
            swap.SetPaymentBroadcast();
            RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentBroadcast);

            Log.Debug("Payment txId {@id}", txId);

            // account new unconfirmed transaction
            await Account
                .UpsertTransactionAsync(
                    tx: swap.PaymentTx,
                    updateBalance: true,
                    notifyIfUnconfirmed: false)
                .ConfigureAwait(false);

            // send payment txId to party
            SwapClient.SwapPaymentAsync(swap);

            if (swap.IsAcceptor)
            {
                var swapOutputs = ((IBitcoinBasedTransaction)swap.PaymentTx)
                    .SwapOutputs()
                    .ToList();

                if (swapOutputs.Count != 1)
                    throw new InternalException(
                        code: Errors.SwapError,
                        description: "Payment tx must have only one swap output");

                // track counter party payment spent event
                TaskPerformer.EnqueueTask(new BitcoinBasedOutputSpentTask
                {
                    Currency = currency,
                    Swap = swap,
                    OutputHash = txId,
                    OutputIndex = swapOutputs.First().Index,
                    Interval = DefaultOutputSpentCheckInterval,
                    CompleteHandler = PaymentSpentEventHandler
                });
            }

            // track payment transaction confirmation
            TaskPerformer.EnqueueTask(new TransactionConfirmationCheckTask
            {
                Currency = currency,
                Swap = swap,
                TxId = txId,
                Interval = DefaultConfirmationCheckInterval,
                CompleteHandler = PaymentConfirmedEventHandler
            });
        }

        private async Task CreatePaymentAsync(ClientSwap swap, int lockTimeInSeconds)
        {
            var lockTime = swap.TimeStamp.ToUniversalTime() + TimeSpan.FromSeconds(lockTimeInSeconds);

            var refundAddress = await Account
                .GetRefundAddressAsync(Currency)
                .ConfigureAwait(false);

            swap.PaymentTx = await CreatePaymentTxAsync(
                    swap: swap,
                    refundAddress: refundAddress.Address,
                    lockTime: lockTime)
                .ConfigureAwait(false);

            swap.PaymentTx = await SignPaymentTxAsync(
                    swap: swap,
                    paymentTx: (IBitcoinBasedTransaction)swap.PaymentTx)
                .ConfigureAwait(false);

            swap.SetPaymentSigned();
            RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentSigned);

            swap.RefundTx = await CreateRefundTxAsync(
                    swap: swap,
                    paymentTx: (IBitcoinBasedTransaction)swap.PaymentTx,
                    refundAddress: refundAddress.Address,
                    lockTime: lockTime)
                .ConfigureAwait(false);

            swap.RefundTx = await SignRefundTxAsync(
                    swap: swap,
                    refundTx: (IBitcoinBasedTransaction)swap.RefundTx,
                    paymentTx: (IBitcoinBasedTransaction)swap.PaymentTx,
                    refundAddress: refundAddress)
                .ConfigureAwait(false);

            swap.SetRefundSigned();
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundSigned);
        }

        public override Task PrepareToReceiveAsync(ClientSwap swap)
        {
            // nothing to do for purchased bitcoin base party
            return Task.CompletedTask;
        }

        public override Task RestoreSwapAsync(ClientSwap swap)
        {
            return swap.IsSoldCurrency(Currency)
                ? RestoreForSoldCurrencyAsync(swap)
                : RestoreForPurchasedCurrencyAsync(swap);
        }

        private async Task RestoreForSoldCurrencyAsync(ClientSwap swap)
        {
            if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                if (!(swap.PaymentTx is IBitcoinBasedTransaction tx))
                {
                    Log.Error("Can't restore swap {@id}. Payment tx is null.", swap.Id);
                    return;
                }

                // check payment transaction spent
                var api = (IInOutBlockchainApi)Currency.BlockchainApi;

                var spentPoint = await api
                    .IsTransactionOutputSpent(tx.Id, tx.SwapOutputs().First().Index) // todo: check specific output 
                    .ConfigureAwait(false);

                if (spentPoint != null && (swap.RefundTx == null ||
                                           (swap.RefundTx != null && spentPoint.Hash != swap.RefundTx.Id)))
                {
                    // raise redeem for counter party in other chain
                    if (swap.IsAcceptor)
                        PaymentSpentEventHandler(swap, spentPoint);
                    // else
                    //    nothing to do (waiting for redeem confirmation in another chain)
                    return;
                }

                if (!(swap.RefundTx is IBitcoinBasedTransaction refundTx))
                {
                    Log.Error("Can't restore swap {@id}. Refund tx is null", swap.Id);
                    return;
                }

                if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast))
                {
                    // wait for refund confirmation
                    TaskPerformer.EnqueueTask(new TransactionConfirmationCheckTask
                    {
                        Currency = Currency,
                        Swap = swap,
                        Interval = DefaultConfirmationCheckInterval,
                        TxId = refundTx.Id,
                        CompleteHandler = RefundConfirmedEventHandler
                    });
                }
                else
                {
                    var refundTimeUtc = swap.TimeStamp.ToUniversalTime() +
                                        TimeSpan.FromSeconds(swap.IsInitiator
                                            ? DefaultInitiatorLockTimeInSeconds
                                            : DefaultAcceptorLockTimeInSeconds);

                    // refund control
                    TaskPerformer.EnqueueTask(new RefundTimeControlTask
                    {
                        Currency = Currency,
                        Swap = swap,
                        Interval = DefaultRefundInterval,
                        RefundTimeUtc = refundTimeUtc,
                        CompleteHandler = RefundTimeControlEventHandler
                    });
                }
            }
            else
            {
                if (DateTime.UtcNow < swap.TimeStamp.ToUniversalTime() + DefaultMaxSwapTimeout)
                {
                    if (swap.IsInitiator)
                    {
                        // todo: initiate swap

                        //await InitiateSwapAsync(swapState)
                        //    .ConfigureAwait(false);
                    }
                    else
                    {
                        // todo: request secret hash from server
                    }
                }
                else
                {
                    swap.Cancel();
                    RaiseSwapUpdated(swap, SwapStateFlags.IsCanceled);
                }
            }
        }

        private Task RestoreForPurchasedCurrencyAsync(ClientSwap swap)
        {
            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast) &&
                !swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemConfirmed))
            {
                if (!(swap.RedeemTx is IBitcoinBasedTransaction redeemTx))
                {
                    Log.Error("Can't restore swap {@id}. Redeem tx is null", swap.Id);
                    return Task.CompletedTask;
                }

                TaskPerformer.EnqueueTask(new TransactionConfirmationCheckTask
                {
                    Currency = Currency,
                    Swap = swap,
                    Interval = DefaultConfirmationCheckInterval,
                    TxId = redeemTx.Id,
                    CompleteHandler = RedeemConfirmedEventHandler
                });
            }

            return Task.CompletedTask;
        }

        public override async Task HandlePartyPaymentAsync(ClientSwap swap, ClientSwap clientSwap)
        {
            Log.Debug("Handle party's payment txId for swap {@swapId}", swap.Id);

            if (swap.PartyPaymentTxId != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Party's payment txId already received for swap {swap.Id}");

            if (clientSwap.PartyPaymentTxId == null)
                throw new InternalException(
                    code: Errors.InvalidPaymentTxId,
                    description: "TxId is null");

            swap.PartyPaymentTxId = clientSwap.PartyPaymentTxId;
            RaiseSwapUpdated(swap, SwapStateFlags.Empty);

            // get party payment tx from block-chain
            var currency = (BitcoinBasedCurrency)swap.PurchasedCurrency;

            var tx = await GetPaymentTxAsync(currency, swap.PartyPaymentTxId)
                .ConfigureAwait(false);

            var refundLockTime = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            if (!BitcoinBasedTransactionVerifier.TryVerifyPartyPaymentTx(
                tx: tx,
                swap: swap,
                secretHash: swap.SecretHash,
                refundLockTime: refundLockTime,
                error: out var error))
            {
                throw new InternalException(error);
            }

            swap.PartyPaymentTx = tx;
            RaiseSwapUpdated(swap, SwapStateFlags.HasPartyPayment);

            // track initiator payment confirmation
            TaskPerformer.EnqueueTask(new TransactionConfirmationCheckTask
            {
                Currency = swap.PurchasedCurrency,
                Swap = swap,
                TxId = swap.PartyPaymentTxId,
                Interval = DefaultConfirmationCheckInterval,
                CompleteHandler = PartyPaymentConfirmedEventHandler
            });
        }

        public override async Task RedeemAsync(ClientSwap swap)
        {
            var currency = swap.PurchasedCurrency;

            var redeemAddress = await Account
                .GetFreeInternalAddressAsync(currency)
                .ConfigureAwait(false);

            // create redeem tx
            swap.RedeemTx = await CreateRedeemTxAsync(
                    swap: swap,
                    paymentTx: (IBitcoinBasedTransaction)swap.PartyPaymentTx,
                    redeemAddress: redeemAddress.Address)
                .ConfigureAwait(false);

            // sign redeem tx
            swap.RedeemTx = await SignRedeemTxAsync(
                    swap: swap,
                    redeemTx: (IBitcoinBasedTransaction)swap.RedeemTx,
                    paymentTx: (IBitcoinBasedTransaction)swap.PartyPaymentTx,
                    redeemAddress: redeemAddress)
                .ConfigureAwait(false);

            swap.SetRedeemSigned();
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemSigned);

            // broadcast redeem tx
            await BroadcastRedeemAsync(
                    swap: swap,
                    redeemTx: swap.RedeemTx)
                .ConfigureAwait(false);

            swap.SetRedeemBroadcast();
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemBroadcast);

            // add new unconfirmed transaction
            await Account
                .UpsertTransactionAsync(
                    tx: swap.RedeemTx,
                    updateBalance: true)
                .ConfigureAwait(false);

            TaskPerformer.EnqueueTask(new TransactionConfirmationCheckTask
            {
                Currency = swap.PurchasedCurrency,
                Swap = swap,
                Interval = DefaultConfirmationCheckInterval,
                TxId = swap.RedeemTx.Id,
                CompleteHandler = RedeemConfirmedEventHandler
            });
        }

        public override Task WaitForRedeemAsync(ClientSwap swap)
        {
            // nothing to do
            return Task.CompletedTask;
        }

        public override Task PartyRedeemAsync(ClientSwap swap)
        {
            // nothing to do
            return Task.CompletedTask;
        }

        private async Task BroadcastRedeemAsync(ClientSwap swap, IBlockchainTransaction redeemTx)
        {
            var currency = swap.PurchasedCurrency;

            var txId = await currency.BlockchainApi
                .BroadcastAsync(redeemTx)
                .ConfigureAwait(false);

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug("Redeem tx {@txId} successfully broadcast for swap {@swapId}", txId, swap.Id);
        }

        private async Task<IBitcoinBasedTransaction> CreatePaymentTxAsync(
            ClientSwap swap,
            string refundAddress,
            DateTimeOffset lockTime)
        {
            var currency = (BitcoinBasedCurrency)swap.SoldCurrency;

            Log.Debug("Create swap payment {@currency} tx for swap {@swapId}",
                currency.Name,
                swap.Id);

            var unspentAddresses = (await Account
                .GetUnspentAddressesAsync(currency)
                .ConfigureAwait(false))
                .ToList()
                .SortList((a, b) => a.AvailableBalance().CompareTo(b.AvailableBalance()))
                .Select(a => a.Address);

            var tx = await _transactionFactory
                .CreateSwapPaymentTxAsync(
                    currency: currency,
                    swap: swap,
                    fromWallets: unspentAddresses,
                    refundAddress: refundAddress,
                    toAddress: swap.PartyAddress,
                    lockTime: lockTime,
                    secretHash: swap.SecretHash,
                    secretSize: DefaultSecretSize,
                    outputsSource: new LocalTxOutputSource(Account))
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Payment tx creation error for swap {swap.Id}");

            Log.Debug("Payment tx successfully created for swap {@swapId}", swap.Id);

            return tx;
        }

        private async Task<IBitcoinBasedTransaction> CreateRefundTxAsync(
            ClientSwap swap,
            IBitcoinBasedTransaction paymentTx,
            string refundAddress,
            DateTimeOffset lockTime)
        {
            Log.Debug("Create refund tx for swap {@swapId}", swap.Id);

            var tx = await _transactionFactory
                .CreateSwapRefundTxAsync(
                    paymentTx: paymentTx,
                    swap: swap,
                    refundAddress: refundAddress,
                    lockTime: lockTime)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Refund tx creation error for swap {swap.Id}");

            Log.Debug("Refund tx successfully created for swap {@swapId}", swap.Id);

            return tx;
        }

        private async Task<IBitcoinBasedTransaction> CreateRedeemTxAsync(
            ClientSwap swap,
            IBitcoinBasedTransaction paymentTx,
            string redeemAddress)
        {
            Log.Debug("Create redeem tx for swap {@swapId}", swap.Id);

            var tx = await _transactionFactory
                .CreateSwapRedeemTxAsync(
                    paymentTx: paymentTx,
                    swap: swap,
                    redeemAddress: redeemAddress)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Redeem tx creation error for swap {swap.Id}");

            return tx;
        }

        private async Task<IBitcoinBasedTransaction> SignPaymentTxAsync(
            ClientSwap swap,
            IBitcoinBasedTransaction paymentTx)
        {
            Log.Debug("Sign payment tx for swap {@swapId}", swap.Id);

            var tx = await new BitcoinBasedSwapSigner(Account)
                .SignPaymentTxAsync(paymentTx)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionSigningError,
                    description: $"Payment tx signing error for swap {swap.Id}");

            Log.Debug("Payment tx successfully signed for swap {@swapId}", swap.Id);

            return tx;
        }

        private async Task<IBitcoinBasedTransaction> SignRefundTxAsync(
            ClientSwap swap,
            IBitcoinBasedTransaction refundTx,
            IBitcoinBasedTransaction paymentTx,
            WalletAddress refundAddress)
        {
            var tx = await new BitcoinBasedSwapSigner(Account)
                .SignRefundTxAsync(
                    refundTx: refundTx,
                    paymentTx: paymentTx,
                    refundAddress: refundAddress)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionSigningError,
                    description: $"Refund tx not signed for swap {swap.Id}");

            return tx;
        }

        private async Task<IBitcoinBasedTransaction> SignRedeemTxAsync(
            ClientSwap swap,
            IBitcoinBasedTransaction redeemTx,
            IBitcoinBasedTransaction paymentTx,
            WalletAddress redeemAddress)
        {
            var tx = await new BitcoinBasedSwapSigner(Account)
                .SignRedeemTxAsync(
                    redeemTx: redeemTx,
                    paymentTx: paymentTx,
                    redeemAddress: redeemAddress,
                    secret: swap.Secret)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionSigningError,
                    description: $"Redeem tx sign error for swap {swap.Id}");

            return tx;
        }

        private async void PaymentConfirmedEventHandler(BackgroundTask task)
        {
            var confirmationCheckTask = task as TransactionConfirmationCheckTask;
            var swap = confirmationCheckTask?.Swap;

            if (swap == null)
                return;

            Log.Debug("Handle payment confirmed event for swap {@swapId}", swap.Id);

            swap.SetPaymentConfirmed();
            RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentConfirmed);

            try
            {
                if (confirmationCheckTask.Transactions.Any())
                {
                    await Account
                        .UpsertTransactionAsync(
                            tx: confirmationCheckTask.Transactions.First(),
                            updateBalance: true)
                        .ConfigureAwait(false);
                }

                var refundTimeUtc = swap.TimeStamp.ToUniversalTime() +
                                    TimeSpan.FromSeconds(swap.IsInitiator
                                        ? DefaultInitiatorLockTimeInSeconds
                                        : DefaultAcceptorLockTimeInSeconds);

                TaskPerformer.EnqueueTask(new RefundTimeControlTask
                {
                    Currency = Currency,
                    Swap = swap,
                    Interval = DefaultRefundInterval,
                    RefundTimeUtc = refundTimeUtc,
                    CompleteHandler = RefundTimeControlEventHandler
                });
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while handle payment tx confirmed event");
            }

            if (swap.IsInitiator)
                RaiseInitiatorPaymentConfirmed(swap);
            else
                RaiseAcceptorPaymentConfirmed(swap);
        }

        private async void PartyPaymentConfirmedEventHandler(BackgroundTask task)
        {
            var confirmationCheckTask = task as TransactionConfirmationCheckTask;
            var swap = confirmationCheckTask?.Swap;

            if (swap == null)
                return;

            Log.Debug("Handle party's payment confirmed event for swap {@swapId}", swap.Id);

            swap.SetPartyPaymentConfirmed();
            RaiseSwapUpdated(swap, SwapStateFlags.IsPartyPaymentConfirmed);

            try
            {
                if (swap.IsInitiator)
                    await RedeemAsync(swap)
                        .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while handle counterParty's payment tx confirmed event");
            }

            if (swap.IsInitiator)
                RaiseAcceptorPaymentConfirmed(swap);
            else
                RaiseInitiatorPaymentConfirmed(swap);
        }

        private async void RefundTimeControlEventHandler(BackgroundTask task)
        {
            var refundTimeControlTask = task as RefundTimeControlTask;
            var swap = refundTimeControlTask?.Swap;

            if (swap == null)
                return;

            try
            {
                var refundTx = (IBitcoinBasedTransaction)swap.RefundTx;

                var txId = await Currency.BlockchainApi
                    .BroadcastAsync(refundTx)
                    .ConfigureAwait(false);

                if (txId == null)
                    throw new Exception("Transaction Id is null");

                Log.Debug("Refund tx id {@txId} for swap {@swapId}", txId, swap.Id);

                // todo: check result

                swap.SetRefundBroadcast();
                RaiseSwapUpdated(swap, SwapStateFlags.IsRefundBroadcast);

                await Account
                    .UpsertTransactionAsync(
                        tx: refundTx,
                        updateBalance: true)
                    .ConfigureAwait(false);

                TaskPerformer.EnqueueTask(new TransactionConfirmationCheckTask
                {
                    Currency = Currency,
                    Swap = swap,
                    Interval = DefaultConfirmationCheckInterval,
                    TxId = swap.RefundTx.Id,
                    CompleteHandler = RefundConfirmedEventHandler
                });
            }
            catch (Exception e)
            {
                Log.Error(e, "Refund task error");
            }
        }

        private void RefundConfirmedEventHandler(BackgroundTask task)
        {
            var confirmationCheckTask = task as TransactionConfirmationCheckTask;
            var swap = confirmationCheckTask?.Swap;

            swap?.SetRefundConfirmed();
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundConfirmed);
        }

        private void RedeemConfirmedEventHandler(BackgroundTask task)
        {
            var confirmationCheckTask = task as TransactionConfirmationCheckTask;
            var swap = confirmationCheckTask?.Swap;

            swap?.SetRedeemConfirmed();
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemConfirmed);
        }

        private void PaymentSpentEventHandler(BackgroundTask task)
        {
            var outputSpentTask = task as BitcoinBasedOutputSpentTask;
            var swap = outputSpentTask?.Swap;

            if (swap == null)
                return;

            Log.Debug("Handle payment spent event for swap {@swapId}", swap.Id);

            PaymentSpentEventHandler(swap, outputSpentTask.SpentPoint);
        }

        private async void PaymentSpentEventHandler(ClientSwap swap, ITxPoint spentPoint)
        {
            try
            {
                if (spentPoint == null)
                    throw new InternalException(
                        code: Errors.InvalidSpentPoint,
                        description: "Invalid spent point");

                // extract secret
                await GetSecretAsync(swap, spentPoint)
                    .ConfigureAwait(false);

                RaiseAcceptorPaymentSpent(swap);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while handle payment tx spent event");
            }
        }

        private async Task<IBitcoinBasedTransaction> GetPaymentTxAsync(
            BitcoinBasedCurrency currency,
            string txId)
        {
            var attempts = 0;

            while (attempts < DefaultGetTransactionAttempts)
            {
                attempts++;

                var tx = (IBitcoinBasedTransaction)await currency.BlockchainApi
                    .GetTransactionAsync(txId)
                    .ConfigureAwait(false);

                if (tx != null)
                    return tx;

                await Task.Delay(DefaultGetTransactionInterval)
                    .ConfigureAwait(false);
            }

            throw new InternalException(
                code: Errors.SwapError,
                description: $"Transaction with id {txId} not found");
        }

        private async Task GetSecretAsync(ClientSwap swap, ITxPoint spentPoint)
        {
            Log.Debug("Try to get CounterParty's payment spent output {@hash}:{@no} for swap {@swapId}",
                spentPoint.Hash,
                spentPoint.Index,
                swap.Id);

            var soldCurrency = swap.SoldCurrency;

            var swapInput = await ((IInOutBlockchainApi)soldCurrency.BlockchainApi)
                .GetInputAsync(spentPoint.Hash, spentPoint.Index)
                .ConfigureAwait(false);

            var secret = swapInput.ExtractSecret();
            var secretHash = CreateSwapSecretHash(secret);

            if (!secretHash.SequenceEqual(swap.SecretHash))
                throw new InternalException(
                    code: Errors.InvalidSecretHash,
                    description: "Invalid secret hash");

            swap.Secret = secret;
            RaiseSwapUpdated(swap, SwapStateFlags.HasSecret);
        }
    }
}