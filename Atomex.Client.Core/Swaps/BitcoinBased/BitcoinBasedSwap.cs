using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.BitcoinBased.Helpers;
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

            await StartWaitForRedeemAsync(swap, cancellationToken)
                .ConfigureAwait(false);
        }

        public override Task StartPartyPaymentControlAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.IsInitiator && 
                swap.HasPartyPayment &&
                !swap.StateFlags.HasFlag(SwapStateFlags.IsPartyPaymentConfirmed))
            {
                // track party payment confirmation
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: swap.PurchasedCurrency,
                        txId: swap.PartyPaymentTxId,
                        confirmationHandler: PartyPaymentConfirmedHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }

            return Task.CompletedTask;
        }

        public override async Task RedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast))
            {
                // redeem already broadcast
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: swap.PurchasedCurrency,
                        txId: swap.RedeemTx.Id,
                        confirmationHandler: RedeemConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();

                return;
            }

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
                    confirmationHandler: RedeemConfirmedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override Task RedeemForPartyAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            // nothing to do
            return Task.CompletedTask;
        }

        public override Task RefundAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast))
            {
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: swap.SoldCurrency,
                        txId: swap.RefundTx.Id,
                        confirmationHandler: RefundConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();

                return Task.CompletedTask;
            }

            swap.RefundTx.ForceBroadcast(
                    swap: swap,
                    interval: DefaultForceRefundInterval,
                    completionHandler: RefundBroadcastEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override Task StartWaitForRedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            BitcoinBasedSwapSpentHelper.StartSwapSpentControlAsync(
                    swap: swap,
                    currency: Currency,
                    refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                    interval: DefaultOutputSpentCheckInterval,
                    completionHandler: PaymentSpentEventHandler,
                    refundTimeReachedHandler: RefundTimeReachedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            if (!swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentConfirmed))
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: Currency,
                        txId: swap.PaymentTxId,
                        confirmationHandler: PaymentConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();

            return Task.CompletedTask;
        }

        public override Task StartWaitForRedeemBySomeoneAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            // nothing to do
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

        private async void PaymentConfirmedEventHandler(
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

        private async void RefundTimeReachedEventHandler(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund time reached for swap {@swapId}", swap.Id);

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

        private async void RefundBroadcastEventHandler(
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
                        confirmationHandler: RefundConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }
            catch (Exception e)
            {
                Log.Error(e, "Refund broadcast handler error");
            }
        }

        private void RefundConfirmedEventHandler(
            ClientSwap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundConfirmed);
        }

        private void RedeemConfirmedEventHandler(
            ClientSwap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemConfirmed);
        }

        private async void PaymentSpentEventHandler(
            ClientSwap swap,
            ITxPoint spentPoint,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle payment spent event for swap {@swapId}", swap.Id);

            try
            {
                var soldCurrency = swap.SoldCurrency;

                var inputResult = await ((IInOutBlockchainApi)soldCurrency.BlockchainApi)
                    .GetInputAsync(spentPoint.Hash, spentPoint.Index, cancellationToken)
                    .ConfigureAwait(false);

                if (inputResult.HasError)
                    throw new InternalException(inputResult.Error.Code, inputResult.Error.Description);

                if (!(inputResult.Value is BitcoinBasedTxPoint spentTxInput))
                    throw new InternalException(Errors.InvalidSpentPoint, "Spent point is not bitcoin based tx point");

                var secret = spentTxInput
                    .ExtractAllPushData()
                    .FirstOrDefault(d =>
                        d.Length == DefaultSecretSize &&
                        CreateSwapSecretHash(d).SequenceEqual(swap.SecretHash));

                if (secret != null)
                {
                    swap.Secret = secret;
                    RaiseSwapUpdated(swap, SwapStateFlags.HasSecret);

                    if (swap.IsAcceptor)
                        RaiseAcceptorPaymentSpent(swap);
                }
                else if (spentTxInput.IsRefund())
                {
                    RefundConfirmedEventHandler(swap, null, cancellationToken);
                }
                else
                {
                    throw new InternalException(
                        Errors.InvalidSpentPoint,
                        $"Unknown redeem or refund script for output {spentPoint.Hash}:{spentPoint.Index}");
                }
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
    }
}