using System;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Common.Abstract;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.BitcoinBased.Tasks;
using Atomex.Swaps.Tasks;
using Atomex.Wallet;
using Atomex.Wallet.Abstract;
using Serilog;

namespace Atomex.Swaps.BitcoinBased
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
                Log.Debug("Acceptor is not ready to broadcast payment tx for swap {@swap} for currency", 
                    swap.Id,
                    Currency);

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
            var broadcastResult = await currency.BlockchainApi
                .BroadcastAsync(swap.PaymentTx)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
            {
                Log.Error("Error while broadcast transaction with code {@code} and description {@description}",
                    broadcastResult.Error.Code, 
                    broadcastResult.Error.Description);
                return;
            }

            var txId = broadcastResult.Value;

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
                var swapOutput = ((IBitcoinBasedTransaction)swap.PaymentTx)
                    .Outputs
                    .Cast<BitcoinBasedTxOutput>()
                    .FirstOrDefault(o => o.IsPayToScriptHash(Convert.FromBase64String(swap.RedeemScript)));

                if (swapOutput == null)
                    throw new InternalException(
                        code: Errors.SwapError,
                        description: "Payment tx have not swap output");
                
                // track counter party payment spent event
                TaskPerformer.EnqueueTask(new BitcoinBasedOutputSpentTask
                {
                    Currency = currency,
                    Swap = swap,
                    OutputHash = txId,
                    OutputIndex = swapOutput.Index,
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

            byte[] redeemScript;

            (swap.PaymentTx, redeemScript) = await CreatePaymentTxAsync(
                    swap: swap,
                    refundAddress: refundAddress.Address,
                    lockTime: lockTime)
                .ConfigureAwait(false);

            swap.RedeemScript = Convert.ToBase64String(redeemScript);

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
                    lockTime: lockTime,
                    redeemScript: redeemScript)
                .ConfigureAwait(false);

            swap.RefundTx = await SignRefundTxAsync(
                    swap: swap,
                    refundTx: (IBitcoinBasedTransaction)swap.RefundTx,
                    paymentTx: (IBitcoinBasedTransaction)swap.PaymentTx,
                    refundAddress: refundAddress,
                    redeemScript: redeemScript)
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

                var swapOutput = ((IBitcoinBasedTransaction)swap.PaymentTx)
                    .Outputs
                    .Cast<BitcoinBasedTxOutput>()
                    .FirstOrDefault(o => o.IsPayToScriptHash(Convert.FromBase64String(swap.RedeemScript)));

                if (swapOutput == null)
                {
                    Log.Error("Can't restore swap {@id}. Payment tx has not swap output");
                    return;
                }

                // check payment transaction spent
                var api = (IInOutBlockchainApi)Currency.BlockchainApi;
                
                var asyncResult = await api
                    .IsTransactionOutputSpent(tx.Id, swapOutput.Index) // todo: check specific output 
                    .ConfigureAwait(false);

                if (asyncResult.HasError)
                {
                    Log.Error("Error while check spent with code {@code} and description {@description}",
                        asyncResult.Error.Code, 
                        asyncResult.Error.Description);
                    return;
                }

                var spentPoint = asyncResult.Value;

                if (spentPoint != null && (swap.RefundTx == null ||
                                           swap.RefundTx != null && spentPoint.Hash != swap.RefundTx.Id))
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
            swap.PartyRedeemScript = clientSwap.PartyRedeemScript;
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

            var partyRedeemScript = Convert.FromBase64String(swap.PartyRedeemScript);

            // create redeem tx
            swap.RedeemTx = await CreateRedeemTxAsync(
                    swap: swap,
                    paymentTx: (IBitcoinBasedTransaction)swap.PartyPaymentTx,
                    redeemAddress: redeemAddress.Address,
                    redeemScript: partyRedeemScript)
                .ConfigureAwait(false);

            // sign redeem tx
            swap.RedeemTx = await SignRedeemTxAsync(
                    swap: swap,
                    redeemTx: (IBitcoinBasedTransaction)swap.RedeemTx,
                    paymentTx: (IBitcoinBasedTransaction)swap.PartyPaymentTx,
                    redeemAddress: redeemAddress,
                    redeemScript: partyRedeemScript)
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

            var asyncResult = await currency.BlockchainApi
                .BroadcastAsync(redeemTx)
                .ConfigureAwait(false);

            if (asyncResult.HasError)
                throw new Exception($"Error while broadcast transaction with code {asyncResult.Error.Code} and description {asyncResult.Error.Description}");

            var txId = asyncResult.Value;

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug("Redeem tx {@txId} successfully broadcast for swap {@swapId}", txId, swap.Id);
        }

        private async Task<(IBitcoinBasedTransaction, byte[])> CreatePaymentTxAsync(
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

            var amount = (long)(AmountHelper.QtyToAmount(swap.Side, swap.Qty, swap.Price) * currency.DigitsMultiplier);

            var (tx, redeemScript) = await _transactionFactory
                .CreateSwapPaymentTxAsync(
                    currency: currency,
                    amount: amount,
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

            tx.Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment;

            Log.Debug("Payment tx successfully created for swap {@swapId}", swap.Id);

            return (tx, redeemScript);
        }

        private async Task<IBitcoinBasedTransaction> CreateRefundTxAsync(
            ClientSwap swap,
            IBitcoinBasedTransaction paymentTx,
            string refundAddress,
            DateTimeOffset lockTime,
            byte[] redeemScript)
        {
            Log.Debug("Create refund tx for swap {@swapId}", swap.Id);

            var amount = (long)(AmountHelper.QtyToAmount(swap.Side, swap.Qty, swap.Price) * Currency.DigitsMultiplier);

            var tx = await _transactionFactory
                .CreateSwapRefundTxAsync(
                    paymentTx: paymentTx,
                    amount: amount,
                    refundAddress: refundAddress,
                    lockTime: lockTime,
                    redeemScript: redeemScript)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Refund tx creation error for swap {swap.Id}");

            tx.Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRefund;

            Log.Debug("Refund tx successfully created for swap {@swapId}", swap.Id);

            return tx;
        }

        private async Task<IBitcoinBasedTransaction> CreateRedeemTxAsync(
            ClientSwap swap,
            IBitcoinBasedTransaction paymentTx,
            string redeemAddress,
            byte[] redeemScript)
        {
            Log.Debug("Create redeem tx for swap {@swapId}", swap.Id);

            var amount = (long)(AmountHelper.QtyToAmount(swap.Side.Opposite(), swap.Qty, swap.Price) * Currency.DigitsMultiplier);

            var tx = await _transactionFactory
                .CreateSwapRedeemTxAsync(
                    paymentTx: paymentTx,
                    amount: amount,
                    redeemAddress: redeemAddress,
                    redeemScript: redeemScript)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Redeem tx creation error for swap {swap.Id}");

            tx.Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem;

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
            WalletAddress refundAddress,
            byte[] redeemScript)
        {
            var tx = await new BitcoinBasedSwapSigner(Account)
                .SignRefundTxAsync(
                    refundTx: refundTx,
                    paymentTx: paymentTx,
                    refundAddress: refundAddress,
                    redeemScript: redeemScript)
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
            WalletAddress redeemAddress,
            byte[] redeemScript)
        {
            var tx = await new BitcoinBasedSwapSigner(Account)
                .SignRedeemTxAsync(
                    redeemTx: redeemTx,
                    paymentTx: paymentTx,
                    redeemAddress: redeemAddress,
                    secret: swap.Secret,
                    redeemScript: redeemScript)
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
                await Account
                    .UpsertTransactionAsync(
                        tx: confirmationCheckTask.Tx,
                        updateBalance: true)
                    .ConfigureAwait(false);

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

        private void RefundTimeControlEventHandler(BackgroundTask task)
        {
            var refundTimeControlTask = task as RefundTimeControlTask;
            var swap = refundTimeControlTask?.Swap;

            if (swap == null)
                return;

            TaskPerformer.EnqueueTask(new BitcoinBasedForceRefundTask
            {
                Currency = Currency,
                Swap = swap,
                Interval = DefaultForceRefundInterval,
                CompleteHandler = RefundBroadcastEventHandler,
            });
        }

        private async void RefundBroadcastEventHandler(BackgroundTask task)
        {
            var refundTask = task as BitcoinBasedForceRefundTask;
            var swap = refundTask?.Swap;

            if (swap == null)
                return;

            try
            {
                Log.Debug("Refund tx id {@txId} for swap {@swapId}", refundTask.RefundTxId, swap.Id);

                // todo: check result

                swap.SetRefundBroadcast();
                RaiseSwapUpdated(swap, SwapStateFlags.IsRefundBroadcast);

                await Account
                    .UpsertTransactionAsync(
                        tx: swap.RefundTx,
                        updateBalance: true)
                    .ConfigureAwait(false);

                TaskPerformer.EnqueueTask(new TransactionConfirmationCheckTask
                {
                    Currency = Currency,
                    Swap = swap,
                    Interval = DefaultConfirmationCheckInterval,
                    TxId = refundTask.RefundTxId,
                    CompleteHandler = RefundConfirmedEventHandler
                });
            }
            catch (Exception e)
            {
                Log.Error(e, "Refund broadcast event handler error");
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

                var asyncResult = await currency.BlockchainApi
                    .GetTransactionAsync(txId)
                    .ConfigureAwait(false);

                if (asyncResult.HasError)
                {
                    Log.Error("Error while get transaction {@txId} with code {@code} and description {@description}", 
                        txId,
                        asyncResult.Error.Code, 
                        asyncResult.Error.Description);
                    return null;
                }

                var tx = asyncResult.Value;

                if (tx != null)
                    return (IBitcoinBasedTransaction)tx;

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

            var asyncResult = await ((IInOutBlockchainApi)soldCurrency.BlockchainApi)
                .GetInputAsync(spentPoint.Hash, spentPoint.Index)
                .ConfigureAwait(false);

            if (asyncResult.HasError)
                throw new InternalException(asyncResult.Error.Code, asyncResult.Error.Description);

            var swapInput = asyncResult.Value;

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