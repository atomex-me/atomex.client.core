using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.BitcoinBased.Helpers;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Helpers;
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
            IBitcoinBasedSwapTransactionFactory transactionFactory)
            : base(currency, account, swapClient)
        {
            _transactionFactory = transactionFactory ??
                throw new ArgumentNullException(nameof(transactionFactory));
        }

        public override async Task PayAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            // 1. check if can broadcast payment
            // 2. create payment tx
            // 3. broadcast payment tx
            // 4. update swap state
            // 5. add payment tx to account
            // 6. send payment tx to party
            // 7. for acceptor start to control output spent
            // 8. control payment confirmation

            if (swap.IsAcceptor && !swap.HasPartyPayment)
            {
                Log.Debug("Acceptor is not ready to broadcast {@currency} payment tx for swap {@swap}",
                    Currency.Name,
                    swap.Id);

                return;
            }

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            await CreatePaymentAsync(swap, lockTimeInSeconds)
                .ConfigureAwait(false);

            Log.Debug("Broadcast {@currency} payment tx for swap {@swap}",
                Currency.Name,
                swap.Id);

            var currency = swap.SoldCurrency;

            // broadcast payment transaction
            var broadcastResult = await currency.BlockchainApi
                .BroadcastAsync(swap.PaymentTx)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
            {
                Log.Error("Error while broadcast {@currency} transaction. Code: {@code}. Description: {@description}",
                    currency.Name,
                    broadcastResult.Error.Code, 
                    broadcastResult.Error.Description);

                return;
            }

            var txId = broadcastResult.Value;

            swap.PaymentTxId = txId ?? throw new Exception("Transaction Id is null");
            swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;
            RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentBroadcast);

            Log.Debug("{@currency} payment txId {@id} for swap {@swap}",
                currency.Name,
                txId,
                swap.Id);

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

                // start acceptor payment spent tracking
                StartOutputSpentControlAsync(
                        swap: swap,
                        currency: currency,
                        txId: txId,
                        index: swapOutput.Index,
                        completionHandler: PaymentSpentHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }

            // start payment transaction confirmation tracking
            TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: currency,
                    txId: txId,
                    confirmationHandler: PaymentConfirmedHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override Task PrepareToReceiveAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            // nothing to do for purchased bitcoin base party
            return Task.CompletedTask;
        }

        public override async Task RedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
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

            var toAddress = await Account
                .ResolveAddressAsync(currency, swap.ToAddress, cancellationToken)
                .ConfigureAwait(false);

            // sign redeem tx
            swap.RedeemTx = await SignRedeemTxAsync(
                    swap: swap,
                    redeemTx: (IBitcoinBasedTransaction)swap.RedeemTx,
                    paymentTx: (IBitcoinBasedTransaction)swap.PartyPaymentTx,
                    redeemAddress: toAddress,
                    redeemScript: partyRedeemScript)
                .ConfigureAwait(false);

            swap.StateFlags |= SwapStateFlags.IsRedeemSigned;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemSigned);

            // broadcast redeem tx
            await BroadcastRedeemAsync(
                    swap: swap,
                    redeemTx: swap.RedeemTx,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemBroadcast);

            // add new unconfirmed transaction
            await Account
                .UpsertTransactionAsync(
                    tx: swap.RedeemTx,
                    updateBalance: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: swap.PurchasedCurrency,
                    txId: swap.RedeemTx.Id,
                    confirmationHandler: RedeemConfirmedHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override Task RefundAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            swap.RefundTx.ForceBroadcast(
                    swap: swap,
                    interval: DefaultForceRefundInterval,
                    completionHandler: RefundBroadcastHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override Task WaitForRedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            // nothing to do
            return Task.CompletedTask;
        }

        public override Task PartyRedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            // nothing to do
            return Task.CompletedTask;
        }

        public override async Task RestoreSwapForSoldCurrencyAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
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

                var outputSpentResult = await api
                    .IsTransactionOutputSpent(tx.Id, swapOutput.Index, cancellationToken) // todo: check specific output 
                    .ConfigureAwait(false);

                if (outputSpentResult.HasError)
                {
                    Log.Error("Error while check spent with code {@code} and description {@description}",
                        outputSpentResult.Error.Code,
                        outputSpentResult.Error.Description);
                    return;
                }

                var spentPoint = outputSpentResult.Value;

                if (spentPoint != null &&
                    (swap.RefundTx == null || swap.RefundTx != null && spentPoint.Hash != swap.RefundTx.Id))
                {
                    // raise redeem for counter party in other chain
                    if (swap.IsAcceptor)
                        PaymentSpentHandler(swap, spentPoint, cancellationToken);
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
                    // start refund confirmation
                    TrackTransactionConfirmationAsync(
                            swap: swap,
                            currency: swap.SoldCurrency,
                            txId: refundTx.Id,
                            confirmationHandler: RefundConfirmedHandler,
                            cancellationToken: cancellationToken)
                        .FireAndForget();
                }
                else
                {
                    var lockTimeInSec = swap.IsInitiator
                        ? DefaultInitiatorLockTimeInSeconds
                        : DefaultAcceptorLockTimeInSeconds;

                    var refundTimeUtc = swap.TimeStamp.ToUniversalTime() + TimeSpan.FromSeconds(lockTimeInSec);

                    // start refund control
                    ControlRefundTimeAsync(
                            swap: swap,
                            refundTimeUtc: refundTimeUtc,
                            refundTimeReachedHandler: RefundTimeReachedHandler,
                            cancellationToken: cancellationToken)
                        .FireAndForget();
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

        public override Task RestoreSwapForPurchasedCurrencyAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast) &&
                !swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemConfirmed))
            {
                if (!(swap.RedeemTx is IBitcoinBasedTransaction redeemTx))
                {
                    Log.Error("Can't restore swap {@id}. Redeem tx is null", swap.Id);
                    return Task.CompletedTask;
                }

                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: swap.PurchasedCurrency,
                        txId: redeemTx.Id,
                        confirmationHandler: RedeemConfirmedHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }

            return Task.CompletedTask;
        }

        public override async Task HandlePartyPaymentAsync(
            ClientSwap swap,
            ClientSwap clientSwap,
            CancellationToken cancellationToken = default)
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

            var tx = await GetPaymentTxAsync(currency, swap.PartyPaymentTxId, cancellationToken)
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
            TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: swap.PurchasedCurrency,
                    txId: swap.PartyPaymentTxId,
                    confirmationHandler: PartyPaymentConfirmedHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
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

            swap.StateFlags |= SwapStateFlags.IsRefundSigned;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundSigned);
        }

        private async Task BroadcastRedeemAsync(
            ClientSwap swap,
            IBlockchainTransaction redeemTx,
            CancellationToken cancellationToken = default)
        {
            var currency = swap.PurchasedCurrency;

            var broadcastResult = await currency.BlockchainApi
                .BroadcastAsync(redeemTx, cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                throw new Exception($"Error while broadcast transaction. Code: {broadcastResult.Error.Code}. Description: {broadcastResult.Error.Description}");

            var txId = broadcastResult.Value;

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
                .SortList(new AvailableBalanceAscending(Account.AssetWarrantyManager))
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

        private async void PaymentConfirmedHandler(
            ClientSwap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle {@currency} payment confirmed event for swap {@swapId}",
                Currency.Name,
                swap.Id);

            try
            {
                swap.StateFlags |= SwapStateFlags.IsPaymentConfirmed;
                RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentConfirmed);

                await Account
                    .UpsertTransactionAsync(
                        tx: tx,
                        updateBalance: true)
                    .ConfigureAwait(false);

                var lockTimeInSec = swap.IsInitiator
                    ? DefaultInitiatorLockTimeInSeconds
                    : DefaultAcceptorLockTimeInSeconds;

                var refundTimeUtc = swap.TimeStamp.ToUniversalTime() + TimeSpan.FromSeconds(lockTimeInSec);

                ControlRefundTimeAsync(
                        swap: swap,
                        refundTimeUtc: refundTimeUtc,
                        refundTimeReachedHandler: RefundTimeReachedHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
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

        private async void PartyPaymentConfirmedHandler(
            ClientSwap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle party's payment confirmed event for swap {@swapId}", swap.Id);

            swap.StateFlags |= SwapStateFlags.IsPartyPaymentConfirmed;
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

        private async void RefundTimeReachedHandler(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await RefundAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Refund time reached handler error");
            }
        }

        private async void RefundBroadcastHandler(
            ClientSwap swap,
            string txId,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund tx id {@txId} for swap {@swapId}", txId, swap.Id);

            try
            {
                swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;
                RaiseSwapUpdated(swap, SwapStateFlags.IsRefundBroadcast);

                await Account
                    .UpsertTransactionAsync(
                        tx: swap.RefundTx,
                        updateBalance: true)
                    .ConfigureAwait(false);

                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: swap.SoldCurrency,
                        txId: txId,
                        confirmationHandler: RefundConfirmedHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }
            catch (Exception e)
            {
                Log.Error(e, "Refund broadcast handler error");
            }
        }

        private void RefundConfirmedHandler(
            ClientSwap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundConfirmed);
        }

        private void RedeemConfirmedHandler(
            ClientSwap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemConfirmed);
        }

        private async void PaymentSpentHandler(
            ClientSwap swap,
            ITxPoint spentPoint,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle payment spent event for swap {@swapId}", swap.Id);

            try
            {
                // extract secret
                await GetSecretAsync(swap, spentPoint, cancellationToken)
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
            string txId,
            CancellationToken cancellationToken = default)
        {
            var attempts = 0;

            while (attempts < DefaultGetTransactionAttempts && !cancellationToken.IsCancellationRequested)
            {
                attempts++;

                var txResult = await currency.BlockchainApi
                    .GetTransactionAsync(txId, cancellationToken)
                    .ConfigureAwait(false);

                if (txResult.HasError && txResult.Error?.Code != (int)HttpStatusCode.NotFound)
                {
                    Log.Error("Error while get transaction {@txId}. Code: {@code}. Description: {@desc}", 
                        txId,
                        txResult.Error.Code,
                        txResult.Error.Description);

                    return null;
                }

                var tx = txResult.Value;

                if (tx != null)
                    return (IBitcoinBasedTransaction)tx;

                await Task.Delay(DefaultGetTransactionInterval, cancellationToken)
                    .ConfigureAwait(false);
            }

            throw new InternalException(
                code: Errors.SwapError,
                description: $"Transaction with id {txId} not found");
        }

        private async Task GetSecretAsync(
            ClientSwap swap,
            ITxPoint spentPoint,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Try to get CounterParty's payment spent output {@hash}:{@no} for swap {@swapId}",
                spentPoint.Hash,
                spentPoint.Index,
                swap.Id);

            var soldCurrency = swap.SoldCurrency;

            var inputResult = await ((IInOutBlockchainApi)soldCurrency.BlockchainApi)
                .GetInputAsync(spentPoint.Hash, spentPoint.Index, cancellationToken)
                .ConfigureAwait(false);

            if (inputResult.HasError)
                throw new InternalException(inputResult.Error.Code, inputResult.Error.Description);

            var swapInput = inputResult.Value;

            var secret = swapInput.ExtractSecret();
            var secretHash = CreateSwapSecretHash(secret);

            if (!secretHash.SequenceEqual(swap.SecretHash))
                throw new InternalException(
                    code: Errors.InvalidSecretHash,
                    description: "Invalid secret hash");

            swap.Secret = secret;
            RaiseSwapUpdated(swap, SwapStateFlags.HasSecret);
        }

        private Task StartOutputSpentControlAsync(
            ClientSwap swap,
            Currency currency,
            string txId,
            uint index,
            Action<ClientSwap, ITxPoint, CancellationToken> completionHandler = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    Log.Debug("Output spent control for {@currency} swap {@swapId}", currency.Name, swap.Id);

                    var result = await currency
                        .GetSpentPointAsync(
                            hash: txId,
                            index: index,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (result.HasError)
                        break;

                    if (result.Value != null)
                    {
                        completionHandler?.Invoke(swap, result.Value, cancellationToken);
                        break;
                    }

                    await Task.Delay(DefaultOutputSpentCheckInterval, cancellationToken)
                        .ConfigureAwait(false);
                }
            }, cancellationToken);
        }
    }
}