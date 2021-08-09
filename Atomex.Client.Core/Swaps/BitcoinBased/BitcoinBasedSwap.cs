using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.Helpers;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.BitcoinBased.Helpers;
using Atomex.Swaps.Helpers;
using Atomex.Wallet;
using Atomex.Wallet.BitcoinBased;

namespace Atomex.Swaps.BitcoinBased
{
    public class BitcoinBasedSwap : CurrencySwap
    {
        private const int MaxInputGettingAttemps = 10;
        private const int InputGettingIntervalInSec = 5;
        private readonly IBitcoinBasedSwapTransactionFactory _transactionFactory;

        private BitcoinBasedConfig Config => Currencies.Get<BitcoinBasedConfig>(Currency);
        private readonly BitcoinBasedAccount _account;

        public BitcoinBasedSwap(
            BitcoinBasedAccount account,
            ICurrencies currencies)
                : base(account.Currency, currencies)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _transactionFactory = new BitcoinBasedSwapTransactionFactory();
        }

        public override async Task PayAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.IsAcceptor && !swap.HasPartyPayment)
            {
                Log.Debug("Acceptor is not ready to broadcast {@currency} payment tx for swap {@swap}",
                    Currency,
                    swap.Id);

                return;
            }

            if (!await CheckPayRelevanceAsync(swap, cancellationToken))
                return;

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var lockTime = swap.TimeStamp.ToUniversalTime() + TimeSpan.FromSeconds(lockTimeInSeconds);

            var refundAddress = await _account
                .GetAddressAsync(swap.RefundAddress)
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

            swap.StateFlags |= SwapStateFlags.IsPaymentSigned;

            await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentSigned)
                .ConfigureAwait(false);

            Log.Debug("Broadcast {@currency} payment tx for swap {@swap}",
                Currency,
                swap.Id);

            var currency = Currencies.GetByName(swap.SoldCurrency);

            // broadcast payment transaction
            var broadcastResult = await currency.BlockchainApi
                .TryBroadcastAsync(swap.PaymentTx, cancellationToken: cancellationToken)
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

            await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentBroadcast, cancellationToken)
                .ConfigureAwait(false);

            Log.Debug("{@currency} payment txId {@id} for swap {@swap}",
                currency.Name,
                txId,
                swap.Id);

            // account new unconfirmed transaction
            await _account
                .UpsertTransactionAsync(
                    tx: swap.PaymentTx,
                    updateBalance: true,
                    notifyIfUnconfirmed: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override Task StartPartyPaymentControlAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start party payment control for swap {@swap}.", swap.Id);

            // initiator waits "accepted" event, acceptor waits "initiated" event
            var initiatedHandler = swap.IsInitiator
                ? new Func<Swap, CancellationToken, Task>(SwapAcceptedHandler)
                : new Func<Swap, CancellationToken, Task>(SwapInitiatedHandler);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();

            _ = BitcoinBasedSwapInitiatedHelper.StartSwapInitiatedControlAsync(
                swap: swap,
                currency: Config,
                refundTimeStamp: refundTimeUtcInSec,
                interval: ConfirmationCheckInterval,
                initiatedHandler: initiatedHandler,
                canceledHandler: SwapCanceledHandler,
                cancellationToken: cancellationToken);

            return Task.CompletedTask;
        }

        public override async Task RedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Redeem for swap {@swap}.", swap.Id);

            var currency = Currencies.GetByName(swap.PurchasedCurrency);

            var needReplaceTx= false;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast))
            {
                Log.Debug("Check redeem confirmation for swap {@swap}.", swap.Id);

                // redeem already broadcast
                var result = await currency
                    .IsTransactionConfirmed(
                        txId: swap.RedeemTx.Id,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (result == null)
                {
                    Log.Error("Error while check bitcoin based redeem tx confirmation. Result is null.");
                    return;
                }
                else if (result.HasError && result.Error.Code == (int)HttpStatusCode.NotFound)
                {
                    // probably the transaction was deleted by miners
                    Log.Debug("Probably the transaction {@tx} was deleted by miners.", swap.RedeemTx.Id);
                    needReplaceTx = true;
                }
                else if (result.HasError)
                {
                    Log.Error("Error while check bitcoin based redeem tx confirmation. Code: {@code}. Description: {@description}.",
                        result.Error.Code,
                        result.Error.Description);

                    return;
                }
                else if (result.Value.IsConfirmed) // tx already confirmed
                {
                    Log.Debug("Transaction {@tx} is already confirmed.", swap.RedeemTx.Id);

                    await RedeemConfirmedEventHandler(swap, result.Value.Transaction, cancellationToken)
                        .ConfigureAwait(false);

                    return;
                }

                var currentTimeUtc = DateTime.UtcNow;

                var creationTimeUtc = swap.RedeemTx.CreationTime != null
                    ? swap.RedeemTx.CreationTime.Value.ToUniversalTime()
                    : swap.TimeStamp.ToUniversalTime();

                var difference = currentTimeUtc - creationTimeUtc;

                Log.Debug("Currenct time: {@current}, creation time: {@now}, difference: {@diff}",
                    currentTimeUtc,
                    creationTimeUtc,
                    difference);

                // check transaction creation time and try replacing it with a higher fee
                if (difference >= TimeSpan.FromHours(4))
                    needReplaceTx = true;

                if (!needReplaceTx)
                {
                    _ = TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: currency,
                        txId: swap.RedeemTx.Id,
                        confirmationHandler: RedeemConfirmedEventHandler,
                        cancellationToken: cancellationToken);

                    return;
                }
            }

            if (swap.IsInitiator)
            {
                var redeemDeadline = swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds) - RedeemTimeReserve;

                if (DateTime.UtcNow > redeemDeadline)
                {
                    Log.Error("Redeem dedline reached for swap {@swap}", swap.Id);
                    return;
                }
            }

            var redeemAddress = await _account
                .GetFreeInternalAddressAsync()
                .ConfigureAwait(false);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds))
                .ToUnixTimeSeconds();

            var bitcoinBased = (BitcoinBasedConfig)currency;

            var partyRedeemScript = swap.PartyRefundAddress == null && swap.PartyRedeemScript != null
                ? new Script(Convert.FromBase64String(swap.PartyRedeemScript))
                : BitcoinBasedSwapTemplate
                    .GenerateHtlcP2PkhSwapPayment(
                        aliceRefundAddress: swap.PartyRefundAddress,
                        bobAddress: swap.ToAddress,
                        lockTimeStamp: refundTimeUtcInSec,
                        secretHash: swap.SecretHash,
                        secretSize: DefaultSecretSize,
                        expectedNetwork: bitcoinBased.Network);

            var side = swap.Symbol
                .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                .Opposite();

            // get party payment
            var partyPaymentResult = await BitcoinBasedSwapInitiatedHelper
                .TryToFindPaymentAsync(
                    swap: swap,
                    currency: currency,
                    side: side,
                    toAddress: swap.ToAddress,
                    refundAddress: swap.PartyRefundAddress,
                    refundTimeStamp: refundTimeUtcInSec,
                    redeemScriptBase64: swap.PartyRedeemScript,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (partyPaymentResult == null || partyPaymentResult.HasError || partyPaymentResult.Value == null)
            {
                Log.Error($"BitcoinBased: can't get party payment for swap {swap.Id}");
                return;
            }

            // create redeem tx
            swap.RedeemTx = await CreateRedeemTxAsync(
                    swap: swap,
                    paymentTx: (IBitcoinBasedTransaction)partyPaymentResult.Value,
                    redeemAddress: redeemAddress.Address,
                    redeemScript: partyRedeemScript.ToBytes(),
                    increaseSequenceNumber: needReplaceTx)
                .ConfigureAwait(false);

            var toAddress = await _account
                .GetAddressAsync(currency.Name, swap.ToAddress, cancellationToken)
                .ConfigureAwait(false);

            // sign redeem tx
            swap.RedeemTx = await SignRedeemTxAsync(
                    swap: swap,
                    redeemTx: (IBitcoinBasedTransaction)swap.RedeemTx,
                    paymentTx: (IBitcoinBasedTransaction)partyPaymentResult.Value,
                    redeemAddress: toAddress,
                    redeemScript: partyRedeemScript.ToBytes())
                .ConfigureAwait(false);

            swap.StateFlags |= SwapStateFlags.IsRedeemSigned;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemSigned, cancellationToken)
                .ConfigureAwait(false);

            // broadcast redeem tx
            await BroadcastRedeemAsync(
                    swap: swap,
                    redeemTx: swap.RedeemTx,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemBroadcast, cancellationToken)
                .ConfigureAwait(false);

            // add new unconfirmed transaction
            await _account
                .UpsertTransactionAsync(
                    tx: swap.RedeemTx,
                    updateBalance: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync(
                swap: swap,
                currency: currency,
                txId: swap.RedeemTx.Id,
                confirmationHandler: RedeemConfirmedEventHandler,
                cancellationToken: cancellationToken);
        }

        public override Task RedeemForPartyAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            // nothing to do
            return Task.CompletedTask;
        }

        public async override Task RefundAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast))
            {
                _ = TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: Currencies.GetByName(swap.SoldCurrency),
                    txId: swap.RefundTx.Id,
                    confirmationHandler: RefundConfirmedEventHandler,
                    cancellationToken: cancellationToken);

                return;
            }

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            DateTimeOffset lockTime = swap.TimeStamp.ToUniversalTime() + TimeSpan.FromSeconds(lockTimeInSeconds);

            var refundAddress = await _account
                .GetAddressAsync(swap.RefundAddress)
                .ConfigureAwait(false);

            var currency = Currencies.Get<BitcoinBasedConfig>(Currency);

            var redeemScript = BitcoinBasedSwapTemplate
                .GenerateHtlcP2PkhSwapPayment(
                    aliceRefundAddress: refundAddress.Address,
                    bobAddress: swap.PartyAddress,
                    lockTimeStamp: lockTime.ToUnixTimeSeconds(),
                    secretHash: swap.SecretHash,
                    secretSize: DefaultSecretSize,
                    expectedNetwork: currency.Network)
                .ToBytes();

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

            await UpdateSwapAsync(swap, SwapStateFlags.IsRefundSigned, cancellationToken)
                .ConfigureAwait(false);

            _ = swap.RefundTx.ForceBroadcast(
                blockchainApi: Config.BlockchainApi,
                swap: swap,
                interval: ForceRefundInterval,
                completionHandler: RefundBroadcastEventHandler,
                cancellationToken: cancellationToken);
        }

        public override Task StartWaitForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var currency = Currencies.GetByName(Currency);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            _ = BitcoinBasedSwapSpentHelper.StartSwapSpentControlAsync(
                swap: swap,
                currency: currency,
                refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                interval: OutputSpentCheckInterval,
                completionHandler: PaymentSpentEventHandler,
                refundTimeReachedHandler: RefundTimeReachedHandler,
                cancellationToken: cancellationToken);

            if (!swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentConfirmed))
                _ = TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: currency,
                    txId: swap.PaymentTxId,
                    confirmationHandler: PaymentConfirmedEventHandler,
                    cancellationToken: cancellationToken);

            return Task.CompletedTask;
        }

        public override Task StartWaitForRedeemBySomeoneAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            // nothing to do
            return Task.CompletedTask;
        }

        public override async Task<Result<IBlockchainTransaction>> TryToFindPaymentAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds))
                .ToUnixTimeSeconds();

            var currency = Currencies
                .GetByName(swap.SoldCurrency);

            var side = swap.Symbol
                .OrderSideForBuyCurrency(swap.PurchasedCurrency);

            return await BitcoinBasedSwapInitiatedHelper
                .TryToFindPaymentAsync(
                    swap: swap,
                    currency: currency,
                    side: side,
                    toAddress: swap.PartyAddress,
                    refundAddress: swap.RefundAddress,
                    refundTimeStamp: refundTimeUtcInSec,
                    redeemScriptBase64: swap.RedeemScript,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task BroadcastRedeemAsync(
            Swap swap,
            IBlockchainTransaction redeemTx,
            CancellationToken cancellationToken = default)
        {
            var currency = Currencies.GetByName(swap.PurchasedCurrency);

            var broadcastResult = await currency.BlockchainApi
                .TryBroadcastAsync(redeemTx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                throw new Exception($"Error while broadcast transaction. Code: {broadcastResult.Error.Code}. Description: {broadcastResult.Error.Description}");

            var txId = broadcastResult.Value;

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug("Redeem tx {@txId} successfully broadcast for swap {@swapId}", txId, swap.Id);
        }

        private async Task<IBitcoinBasedTransaction> CreatePaymentTxAsync(
            Swap swap,
            string refundAddress,
            DateTimeOffset lockTime)
        {
            var currency = Currencies.Get<BitcoinBasedConfig>(swap.SoldCurrency);

            Log.Debug("Create swap payment {@currency} tx for swap {@swapId}",
                currency.Name,
                swap.Id);

            var unspentAddresses = (await _account
                .GetUnspentAddressesAsync()
                .ConfigureAwait(false))
                .ToList()
                .SortList(new AvailableBalanceAscending())
                .Select(a => a.Address);

            var amountInSatoshi = currency.CoinToSatoshi(
                AmountHelper.QtyToAmount(
                    swap.Side,
                    swap.Qty,
                    swap.Price,
                    currency.DigitsMultiplier));

            // maker network fee
            if (swap.MakerNetworkFee > 0)
            {
                var makerNetworkFeeInSatoshi = currency.CoinToSatoshi(swap.MakerNetworkFee);

                if (makerNetworkFeeInSatoshi < amountInSatoshi) // network fee size check
                    amountInSatoshi += makerNetworkFeeInSatoshi;
            }

            var tx = await _transactionFactory
                .CreateSwapPaymentTxAsync(
                    currency: currency,
                    amount: amountInSatoshi,
                    fromWallets: unspentAddresses,
                    refundAddress: refundAddress,
                    toAddress: swap.PartyAddress,
                    lockTime: lockTime,
                    secretHash: swap.SecretHash,
                    secretSize: DefaultSecretSize,
                    outputsSource: new LocalTxOutputSource(_account))
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Payment tx creation error for swap {swap.Id}");

            tx.Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment;

            Log.Debug("Payment tx successfully created for swap {@swapId}", swap.Id);

            return tx;
        }

        private async Task<IBitcoinBasedTransaction> CreateRefundTxAsync(
            Swap swap,
            IBitcoinBasedTransaction paymentTx,
            string refundAddress,
            DateTimeOffset lockTime,
            byte[] redeemScript)
        {
            Log.Debug("Create refund tx for swap {@swapId}", swap.Id);

            var currency = Currencies.Get<BitcoinBasedConfig>(Currency);

            var amountInSatoshi = currency.CoinToSatoshi(
                AmountHelper.QtyToAmount(
                    swap.Side,
                    swap.Qty,
                    swap.Price,
                    currency.DigitsMultiplier));

            var tx = await _transactionFactory
                .CreateSwapRefundTxAsync(
                    currency: currency,
                    paymentTx: paymentTx,
                    amount: amountInSatoshi,
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
            Swap swap,
            IBitcoinBasedTransaction paymentTx,
            string redeemAddress,
            byte[] redeemScript,
            bool increaseSequenceNumber = false)
        {
            Log.Debug("Create redeem tx for swap {@swapId}", swap.Id);

            var currency = Currencies.Get<BitcoinBasedConfig>(Currency);

            var amountInSatoshi = currency.CoinToSatoshi(
                AmountHelper.QtyToAmount(
                    swap.Side.Opposite(),
                    swap.Qty,
                    swap.Price,
                    currency.DigitsMultiplier));

            var sequenceNumber = 0u;

            if (increaseSequenceNumber)
            {
                var previousSequenceNumber = (swap?.RedeemTx as IBitcoinBasedTransaction)?.GetSequenceNumber(0) ?? 0;

                sequenceNumber = previousSequenceNumber == 0
                    ? Sequence.SEQUENCE_FINAL - 1024
                    : (previousSequenceNumber == Sequence.SEQUENCE_FINAL
                        ? Sequence.SEQUENCE_FINAL
                        : previousSequenceNumber + 1);
            }

            var tx = await _transactionFactory
                .CreateSwapRedeemTxAsync(
                    currency: currency,
                    paymentTx: paymentTx,
                    amount: amountInSatoshi,
                    redeemAddress: redeemAddress,
                    redeemScript: redeemScript,
                    sequenceNumber: sequenceNumber)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Redeem tx creation error for swap {swap.Id}");

            tx.Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem;

            return tx;
        }

        private async Task<IBitcoinBasedTransaction> SignPaymentTxAsync(
            Swap swap,
            IBitcoinBasedTransaction paymentTx)
        {
            Log.Debug("Sign payment tx for swap {@swapId}", swap.Id);

            var tx = await new BitcoinBasedSwapSigner(_account)
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
            Swap swap,
            IBitcoinBasedTransaction refundTx,
            IBitcoinBasedTransaction paymentTx,
            WalletAddress refundAddress,
            byte[] redeemScript)
        {
            var tx = await new BitcoinBasedSwapSigner(_account)
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
            Swap swap,
            IBitcoinBasedTransaction redeemTx,
            IBitcoinBasedTransaction paymentTx,
            WalletAddress redeemAddress,
            byte[] redeemScript)
        {
            var tx = await new BitcoinBasedSwapSigner(_account)
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

        private async Task PaymentConfirmedEventHandler(
            Swap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle {@currency} payment confirmed event for swap {@swapId}",
                Currency,
                swap.Id);

            try
            {
                swap.StateFlags |= SwapStateFlags.IsPaymentConfirmed;

                await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentConfirmed, cancellationToken)
                    .ConfigureAwait(false);

                await _account
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
                await RaiseInitiatorPaymentConfirmed(swap, cancellationToken)
                    .ConfigureAwait(false);
            else
                await RaiseAcceptorPaymentConfirmed(swap, cancellationToken)
                    .ConfigureAwait(false);
        }

        protected override async Task RefundTimeReachedHandler(
            Swap swap,
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
            Swap swap,
            string txId,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund tx id {@txId} for swap {@swapId}", txId, swap.Id);

            try
            {
                swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;

                await UpdateSwapAsync(swap, SwapStateFlags.IsRefundBroadcast, cancellationToken)
                    .ConfigureAwait(false);

                await _account
                    .UpsertTransactionAsync(
                        tx: swap.RefundTx,
                        updateBalance: true)
                    .ConfigureAwait(false);

                _ = TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: Currencies.GetByName(swap.SoldCurrency),
                    txId: txId,
                    confirmationHandler: RefundConfirmedEventHandler,
                    cancellationToken: cancellationToken);
            }
            catch (Exception e)
            {
                Log.Error(e, "Refund broadcast handler error");
            }
        }

        private async Task PaymentSpentEventHandler(
            Swap swap,
            ITxPoint spentPoint,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle payment spent event for swap {@swapId}", swap.Id);

            try
            {
                var soldCurrency = Currencies.GetByName(swap.SoldCurrency);

                BitcoinBasedTxPoint spentTxInput = null;
                var attempts = 0;

                while (attempts < MaxInputGettingAttemps)
                {
                    attempts++;

                    var inputResult = await ((IInOutBlockchainApi)soldCurrency.BlockchainApi)
                        .TryGetInputAsync(spentPoint.Hash, spentPoint.Index, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (inputResult == null || (inputResult.HasError && inputResult.Error?.Code == Errors.RequestError))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(InputGettingIntervalInSec), cancellationToken)
                            .ConfigureAwait(false);

                        continue;
                    }

                    if (inputResult.HasError)
                        throw new InternalException(inputResult.Error.Code, inputResult.Error.Description);

                    spentTxInput = inputResult.Value as BitcoinBasedTxPoint;

                    if (spentTxInput == null)
                        throw new InternalException(Errors.InvalidSpentPoint, "Spent point is not bitcoin based tx point");

                    break;
                }

                var secret = spentTxInput
                    .ExtractAllPushData()
                    .FirstOrDefault(d =>
                        d.Length == DefaultSecretSize &&
                        CreateSwapSecretHash(d).SequenceEqual(swap.SecretHash));

                if (secret != null)
                {
                    swap.Secret = secret;

                    await UpdateSwapAsync(swap, SwapStateFlags.HasSecret, cancellationToken)
                        .ConfigureAwait(false);

                    if (swap.IsAcceptor)
                        await RaiseAcceptorPaymentSpent(swap, cancellationToken)
                            .ConfigureAwait(false);
                }
                else if (spentTxInput.IsRefund())
                {
                    await RefundConfirmedEventHandler(swap, null, cancellationToken)
                        .ConfigureAwait(false);
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
    }
}