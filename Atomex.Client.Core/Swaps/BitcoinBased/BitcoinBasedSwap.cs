using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Blockchain.Helpers;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.BitcoinBased.Helpers;
using Atomex.Swaps.Helpers;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallets;

namespace Atomex.Swaps.BitcoinBased
{
    public class BitcoinBasedSwap : CurrencySwap
    {
        private const int MaxInputGettingAttemps = 10;
        private const int InputGettingIntervalInSec = 5;
        private readonly IBitcoinBasedSwapTransactionFactory _transactionFactory;

        private BitcoinBasedConfig Config => Currencies.Get<BitcoinBasedConfig>(Currency);
        private readonly BitcoinBasedAccount _account;
        private readonly bool _allowSpendingAllOutputs;

        public BitcoinBasedSwap(
            BitcoinBasedAccount account,
            ICurrencies currencies,
            bool allowSpendingAllOutputs = false)
                : base(account.Currency, currencies)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _transactionFactory = new BitcoinBasedSwapTransactionFactory();
            _allowSpendingAllOutputs = allowSpendingAllOutputs;
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

            var paymentTx = await CreatePaymentTxAsync(
                    swap: swap,
                    refundAddress: refundAddress.Address,
                    lockTime: lockTime)
                .ConfigureAwait(false);

            paymentTx = await SignPaymentTxAsync(
                    swap: swap,
                    paymentTx: paymentTx)
                .ConfigureAwait(false);

            swap.StateFlags |= SwapStateFlags.IsPaymentSigned;

            await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentSigned)
                .ConfigureAwait(false);

            Log.Debug("Broadcast {@currency} payment tx for swap {@swap}",
                Currency,
                swap.Id);

            var api = Config.GetBitcoinBlockchainApi();

            // broadcast payment transaction
            var (txId, error) = await api
                .BroadcastAsync(paymentTx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error("Error while broadcast {@currency} transaction. Code: {@code}. Message: {@message}",
                    Currency,
                    error.Value.Code,
                    error.Value.Message);

                return;
            }

            swap.PaymentTxId = txId ?? throw new Exception("Transaction Id is null");
            swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentBroadcast, cancellationToken)
                .ConfigureAwait(false);

            Log.Debug("{@currency} payment txId {@id} for swap {@swap}",
                Currency,
                txId,
                swap.Id);

            // account new unconfirmed transaction
            await _account
                .LocalStorage
                .UpsertTransactionAsync(
                    tx: paymentTx,
                    notifyIfNewOrChanged: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override Task StartPartyPaymentControlAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start party {@currency} payment control for swap {@swap}", Currency, swap.Id);

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
            Log.Debug("Redeem for swap {@swap}", swap.Id);

            var currency = Currencies.GetByName(swap.PurchasedCurrency);

            BitcoinTransaction redeemTx = null;
            var needReplaceTx= false;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast))
            {
                Log.Debug("Check redeem confirmation for swap {@swap}", swap.Id);

                // redeem already broadcast
                var (result, error) = await currency
                    .IsTransactionConfirmed(
                        txId: swap.RedeemTxId,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null && error.Value.Code == (int)HttpStatusCode.NotFound)
                {
                    // probably the transaction was deleted by miners
                    Log.Debug("Probably the transaction {@tx} was deleted by miners", swap.RedeemTxId);
                    needReplaceTx = true;
                }
                else if (error != null)
                {
                    Log.Error("Error while check bitcoin based redeem tx confirmation. Code: {@code}. Message: {@message}",
                        error.Value.Code,
                        error.Value.Message);

                    return;
                }
                else if (result == null)
                {
                    Log.Error("Error while check bitcoin based redeem tx confirmation. Result is null");
                    return;
                }
                else if (result.IsConfirmed) // tx already confirmed
                {
                    Log.Debug("Transaction {@tx} is already confirmed", swap.RedeemTxId);

                    await RedeemConfirmedEventHandler(swap, cancellationToken)
                        .ConfigureAwait(false);

                    return;
                }

                var (existsRedeemTx, existsRedeemTxError) = await TransactionsHelper
                    .TryFindTransaction<BitcoinTransaction>(
                        txId: swap.RedeemTxId,
                        currency: Currency,
                        localStorage: _account.LocalStorage,
                        blockchainApi: Config.GetBlockchainApi(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (existsRedeemTxError == null && existsRedeemTx != null)
                    redeemTx = existsRedeemTx;

                var currentTimeUtc = DateTime.UtcNow;

                var creationTimeUtc = redeemTx?.CreationTime != null
                    ? redeemTx.CreationTime.Value.ToUniversalTime()
                    : swap.TimeStamp.ToUniversalTime();

                var difference = currentTimeUtc - creationTimeUtc;

                Log.Debug("Current time: {@current}, creation time: {@now}, difference: {@diff}",
                    currentTimeUtc,
                    creationTimeUtc,
                    difference);

                // check transaction creation time and try replacing it with a higher fee
                if (difference >= TimeSpan.FromHours(4))
                    needReplaceTx = true;

                if (!needReplaceTx)
                {
                    _ = TrackTransactionConfirmationAsync<BitcoinTransaction>(
                        swap: swap,
                        localStorage: _account.LocalStorage,
                        txId: swap.RedeemTxId,
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
                    Log.Error("Redeem deadline reached for swap {@swap}", swap.Id);
                    return;
                }
            }

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds))
                .ToUnixTimeSeconds();

            var bitcoinBased = (BitcoinBasedConfig)currency;

            var partyRedeemScript = swap.PartyRefundAddress == null && swap.PartyRedeemScript != null
                ? new Script(Convert.FromBase64String(swap.PartyRedeemScript))
                : BitcoinSwapTemplate.CreateHtlcSwapPayment(
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
            var (partyPaymentTx, searchError) = await BitcoinBasedSwapInitiatedHelper
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

            if (searchError != null || partyPaymentTx == null)
            {
                Log.Error($"BitcoinBased: can't get party payment for swap {swap.Id}");
                return;
            }

            var redeemToAddress = swap.RedeemFromAddress ?? (await _account
                .GetFreeInternalAddressAsync()
                .ConfigureAwait(false))
                .Address;

            // create redeem tx
            redeemTx = await CreateRedeemTxAsync(
                    swap: swap,
                    paymentTx: partyPaymentTx,
                    redeemAddress: redeemToAddress,
                    redeemScript: partyRedeemScript.ToBytes(),
                    increaseSequenceNumber: needReplaceTx,
                    previousSequenceNumber: redeemTx?.GetSequenceNumber(0) ?? 0)
                .ConfigureAwait(false);

            var toAddress = await _account
                .GetAddressAsync(swap.ToAddress, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // sign redeem tx
            redeemTx = await SignRedeemTxAsync(
                    swap: swap,
                    redeemTx: redeemTx,
                    paymentTx: partyPaymentTx,
                    redeemAddress: toAddress,
                    redeemScript: partyRedeemScript.ToBytes())
                .ConfigureAwait(false);

            swap.StateFlags |= SwapStateFlags.IsRedeemSigned;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemSigned, cancellationToken)
                .ConfigureAwait(false);

            // broadcast redeem tx
            await BroadcastRedeemAsync(
                    swap: swap,
                    redeemTx: redeemTx,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            swap.RedeemTxId = redeemTx.Id;
            swap.LastRedeemTryTimeStamp = DateTime.UtcNow;
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemBroadcast, cancellationToken)
                .ConfigureAwait(false);

            // add new unconfirmed transaction
            await _account
                .LocalStorage
                .UpsertTransactionAsync(
                    tx: redeemTx,
                    notifyIfNewOrChanged: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync<BitcoinTransaction>(
                swap: swap,
                localStorage: _account.LocalStorage,
                txId: swap.RedeemTxId,
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
                _ = TrackTransactionConfirmationAsync<BitcoinTransaction>(
                    swap: swap,
                    localStorage: _account.LocalStorage,
                    txId: swap.RefundTxId,
                    confirmationHandler: RefundConfirmedEventHandler,
                    cancellationToken: cancellationToken);

                return;
            }

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var lockTime = swap.TimeStamp.ToUniversalTime() + TimeSpan.FromSeconds(lockTimeInSeconds);

            await RefundTimeDelayAsync(lockTime, cancellationToken)
                .ConfigureAwait(false);

            var refundAddress = await _account
                .GetAddressAsync(swap.RefundAddress)
                .ConfigureAwait(false);

            var currency = Currencies.Get<BitcoinBasedConfig>(Currency);

            var redeemScript = BitcoinSwapTemplate
                .CreateHtlcSwapPayment(
                    aliceRefundAddress: refundAddress.Address,
                    bobAddress: swap.PartyAddress,
                    lockTimeStamp: lockTime.ToUnixTimeSeconds(),
                    secretHash: swap.SecretHash,
                    secretSize: DefaultSecretSize,
                    expectedNetwork: currency.Network)
                .ToBytes();

            var (paymentTx, paymentTxFindError) = await TransactionsHelper
                .TryFindTransaction<BitcoinTransaction>(
                    txId: swap.PaymentTxId,
                    currency: Currency,
                    localStorage: _account.LocalStorage,
                    blockchainApi: Config.GetBlockchainApi(),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (paymentTxFindError != null || paymentTx == null)
            {
                Log.Error("Can't find payment tx with {@id}", swap.PaymentTxId);
                return;
            }

            var refundTx = await CreateRefundTxAsync(
                    swap: swap,
                    paymentTx: paymentTx,
                    refundAddress: refundAddress.Address,
                    lockTime: lockTime,
                    redeemScript: redeemScript)
                .ConfigureAwait(false);

            refundTx = await SignRefundTxAsync(
                    swap: swap,
                    refundTx: refundTx,
                    paymentTx: paymentTx,
                    refundAddress: refundAddress,
                    redeemScript: redeemScript)
                .ConfigureAwait(false);

            swap.StateFlags |= SwapStateFlags.IsRefundSigned;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRefundSigned, cancellationToken)
                .ConfigureAwait(false);

            _ = refundTx.ForceBroadcast(
                blockchainApi: Config.GetBitcoinBlockchainApi(),
                swap: swap,
                interval: ForceRefundInterval,
                completionHandler: RefundBroadcastEventHandler,
                cancellationToken: cancellationToken);
        }

        public override Task StartWaitingForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start waiting for {@currency} redeem for swap {@swap}", Currency, swap.Id);

            var currency = Currencies.GetByName(Currency);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            _ = Task.Run(() => BitcoinBasedSwapSpentHelper.StartSwapSpentControlAsync(
                swap: swap,
                currencyConfig: Config,
                localStorage: _account.LocalStorage,
                refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                interval: OutputSpentCheckInterval,
                completionHandler: PaymentSpentEventHandler,
                refundTimeReachedHandler: RefundTimeReachedHandler,
                cancellationToken: cancellationToken), cancellationToken);

            if (!swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentConfirmed))
                _ = TrackTransactionConfirmationAsync<BitcoinTransaction>(
                    swap: swap,
                    localStorage: _account.LocalStorage,
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

        public override async Task<Result<ITransaction>> TryToFindPaymentAsync(
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

            var (paymentTx, error) = await BitcoinBasedSwapInitiatedHelper
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

            if (error != null)
                return error;

            return paymentTx;
        }

        private async Task BroadcastRedeemAsync(
            Swap swap,
            BitcoinTransaction redeemTx,
            CancellationToken cancellationToken = default)
        {
            var api = Config.GetBitcoinBlockchainApi();

            var (txId, error) = await api
                .BroadcastAsync(redeemTx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                throw new Exception($"Error while broadcast transaction. Code: {error.Value.Code}. Message: {error.Value.Message}");

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug("Redeem tx {@txId} successfully broadcast for swap {@swapId}", txId, swap.Id);
        }

        private async Task<BitcoinTransaction> CreatePaymentTxAsync(
            Swap swap,
            string refundAddress,
            DateTimeOffset lockTime)
        {
            var currency = Currencies.Get<BitcoinBasedConfig>(swap.SoldCurrency);

            Log.Debug("Create swap payment {@currency} tx for swap {@swapId}",
                currency.Name,
                swap.Id);

            var amountInSatoshi = currency.CoinToSatoshi(
                AmountHelper.QtyToSellAmount(
                    swap.Side,
                    swap.Qty,
                    swap.Price,
                    currency.Precision));

            // maker network fee
            if (swap.MakerNetworkFee > 0)
            {
                var makerNetworkFeeInSatoshi = currency.CoinToSatoshi(swap.MakerNetworkFee);

                if (makerNetworkFeeInSatoshi < amountInSatoshi) // network fee size check
                    amountInSatoshi += makerNetworkFeeInSatoshi;
            }

            // check from outputs
            var availableOutputs = await _account
                .GetAvailableOutputsAsync()
                .ConfigureAwait(false);

            var outputsAlreadySpent = swap.FromOutputs
                .Any(o => availableOutputs.FirstOrDefault(ao => ao.TxId == o.Hash && ao.Index == o.Index) == null);

            if (outputsAlreadySpent)
            {
                Log.Debug($"Some outputs already spent for {Currency} swap payment");

                swap.FromOutputs
                    .Where(o => availableOutputs.FirstOrDefault(ao => ao.TxId == o.Hash && ao.Index == o.Index) != null)
                    .ToList();
            }

            var fromOutputs = swap.FromOutputs
                .Select(o => availableOutputs.First(ao => ao.TxId == o.Hash && ao.Index == o.Index));

            var fromOutputsInSatoshi = fromOutputs.Sum(o => o.Value);

            if (fromOutputsInSatoshi < amountInSatoshi && _allowSpendingAllOutputs)
                fromOutputs = availableOutputs;

            var tx = await _transactionFactory
                .CreateSwapPaymentTxAsync(
                    fromOutputs: fromOutputs,
                    amount: amountInSatoshi,
                    refundAddress: refundAddress,
                    toAddress: swap.PartyAddress,
                    lockTime: lockTime,
                    secretHash: swap.SecretHash,
                    secretSize: DefaultSecretSize,
                    currencyConfig: currency)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Payment tx creation error for swap {swap.Id}");

            Log.Debug("Payment tx successfully created for swap {@swapId}", swap.Id);

            return tx;
        }

        private async Task<BitcoinTransaction> CreateRefundTxAsync(
            Swap swap,
            BitcoinTransaction paymentTx,
            string refundAddress,
            DateTimeOffset lockTime,
            byte[] redeemScript)
        {
            Log.Debug("Create refund tx for swap {@swapId}", swap.Id);

            var currency = Currencies.Get<BitcoinBasedConfig>(Currency);

            var amountInSatoshi = currency.CoinToSatoshi(
                AmountHelper.QtyToSellAmount(
                    swap.Side,
                    swap.Qty,
                    swap.Price,
                    currency.Precision));

            var tx = await _transactionFactory
                .CreateSwapRefundTxAsync(
                    paymentTx: paymentTx,
                    amount: amountInSatoshi,
                    refundAddress: refundAddress,
                    redeemScript: redeemScript,
                    lockTime: lockTime,
                    currency: currency)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Refund tx creation error for swap {swap.Id}");

            Log.Debug("Refund tx successfully created for swap {@swapId}", swap.Id);

            return tx;
        }

        private async Task<BitcoinTransaction> CreateRedeemTxAsync(
            Swap swap,
            BitcoinTransaction paymentTx,
            string redeemAddress,
            byte[] redeemScript,
            bool increaseSequenceNumber = false,
            uint previousSequenceNumber = 0)
        {
            Log.Debug("Create redeem tx for swap {@swapId}", swap.Id);

            var currency = Currencies.Get<BitcoinBasedConfig>(Currency);

            var amountInSatoshi = currency.CoinToSatoshi(
                AmountHelper.QtyToSellAmount(
                    swap.Side.Opposite(),
                    swap.Qty,
                    swap.Price,
                    currency.Precision));

            var sequenceNumber = 0u;

            if (increaseSequenceNumber)
            {
                sequenceNumber = previousSequenceNumber == 0
                    ? Sequence.SEQUENCE_FINAL - 1024
                    : (previousSequenceNumber == Sequence.SEQUENCE_FINAL
                        ? Sequence.SEQUENCE_FINAL
                        : previousSequenceNumber + 1);
            }

            var tx = await _transactionFactory
                .CreateSwapRedeemTxAsync(
                    paymentTx: paymentTx,
                    amount: amountInSatoshi,
                    redeemAddress: redeemAddress,
                    redeemScript: redeemScript,
                    currency: currency,
                    sequenceNumber: sequenceNumber)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Redeem tx creation error for swap {swap.Id}");

            return tx;
        }

        private async Task<BitcoinTransaction> SignPaymentTxAsync(
            Swap swap,
            BitcoinTransaction paymentTx)
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

        private async Task<BitcoinTransaction> SignRefundTxAsync(
            Swap swap,
            BitcoinTransaction refundTx,
            BitcoinTransaction paymentTx,
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

        private async Task<BitcoinTransaction> SignRedeemTxAsync(
            Swap swap,
            BitcoinTransaction redeemTx,
            BitcoinTransaction paymentTx,
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
            BitcoinTransaction tx,
            string txId,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund tx id {@txId} for swap {@swapId}", txId, swap.Id);

            try
            {
                swap.RefundTxId = txId;
                swap.LastRefundTryTimeStamp = DateTime.UtcNow;
                swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;

                await UpdateSwapAsync(swap, SwapStateFlags.IsRefundBroadcast, cancellationToken)
                    .ConfigureAwait(false);

                await _account
                    .LocalStorage
                    .UpsertTransactionAsync(
                        tx: tx,
                        notifyIfNewOrChanged: true,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                _ = TrackTransactionConfirmationAsync<BitcoinTransaction>(
                    swap: swap,
                    localStorage: _account.LocalStorage,
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
            BitcoinTxPoint spentPoint,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle payment spent event for swap {@swapId}", swap.Id);

            try
            {
                //var soldCurrency = Currencies.GetByName(swap.SoldCurrency);

                BitcoinTxInput spentTxInput = null;
                var attempts = 0;

                while (attempts < MaxInputGettingAttemps)
                {
                    attempts++;

                    var (input, error) = await Config
                        .GetBitcoinBlockchainApi()
                        .GetInputAsync(spentPoint.Hash, spentPoint.Index, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if ((error != null && error.Value.Code == Errors.RequestError) || input == null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(InputGettingIntervalInSec), cancellationToken)
                            .ConfigureAwait(false);

                        continue;
                    }

                    if (error != null)
                        throw new InternalException(error.Value.Code, error.Value.Message);

                    spentTxInput = input ?? throw new InternalException(Errors.InvalidSpentPoint, "Spent point is null");

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
                    var (spentTx, error) = await Config
                        .GetBlockchainApi()
                        .GetTransactionAsync(spentPoint.Hash)
                        .ConfigureAwait(false);

                    if (error == null && spentTx != null && spentTx.IsConfirmed)
                        await RefundConfirmedEventHandler(swap, cancellationToken)
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