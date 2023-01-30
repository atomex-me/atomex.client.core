using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Dto.Swaps.V1;
using Atomex.Blockchain.Ethereum.EtherScan;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Swaps.Ethereum.Helpers
{
    public static class EthereumSwapInitiatedHelper
    {
        private const int BlocksAhead = 10000; // >24h with block time 10-30 seconds

        public static async Task<Result<ITransaction>> TryToFindPaymentAsync(
            Swap swap,
            EthereumConfig currencyConfig,
            CancellationToken cancellationToken = default)
        {
            var api = currencyConfig.GetEtherScanApi();

            var (fromBlockNo, fromBlockError) = await api
                .GetBlockNumberAsync(
                    timeStamp: swap.TimeStamp.ToUniversalTime(),
                    blockClosest: ClosestBlock.Before,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (fromBlockError != null)
                return fromBlockError;

            var (txs, error) = await api
                .GetTransactionsAsync(
                    address: currencyConfig.SwapContractAddress,
                    fromBlock: fromBlockNo,
                    toBlock: fromBlockNo + BlocksAhead,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            foreach (var tx in txs.Cast<EthereumTransaction>())
            {
                if (!tx.From.Equals(swap.FromAddress, StringComparison.OrdinalIgnoreCase) ||
                   !tx.To.Equals(currencyConfig.SwapContractAddress, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!tx.Data.Contains(swap.SecretHash.ToHexString()) ||
                    !tx.Data.Contains(swap.PartyAddress))
                    continue;

                if (tx.Status == TransactionStatus.Failed)
                    continue; // skip failed transactions

                return tx;
            }

            return new Result<ITransaction> { Value = null };
        }

        public static async Task<Result<bool>> IsInitiatedAsync(
            Swap swap,
            EthereumConfig ethereumConfig,
            long refundTimeStamp,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum: check initiated event");

                var sideOpposite = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var requiredAmountInEth = AmountHelper.QtyToSellAmount(sideOpposite, swap.Qty, swap.Price, ethereumConfig.DigitsMultiplier);
                var requiredAmountInWei = EthereumHelper.EthToWei(requiredAmountInEth);
                var requiredRewardForRedeemInWei = EthereumHelper.EthToWei(swap.RewardForRedeem);

                var api = ethereumConfig.GetEtherScanApi();

                var (events, error) = await api
                    .GetContractEventsAsync(
                        address: ethereumConfig.SwapContractAddress,
                        fromBlock: ethereumConfig.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<InitiatedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        topic2: "0x000000000000000000000000" + swap.ToAddress[2..],
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;

                if (events == null || !events.Any())
                    return false;

                var initiatedEvent = events.First().ParseInitiatedEvent();

                if (initiatedEvent.Value >= requiredAmountInWei - requiredRewardForRedeemInWei)
                {
                    if (initiatedEvent.RefundTimestamp < refundTimeStamp)
                    {
                        Log.Debug(
                            "Invalid refund time in initiated event. Expected value is {@expected}, actual is {@actual}",
                            refundTimeStamp,
                            (long)initiatedEvent.RefundTimestamp);

                        return new Error(
                            code: Errors.InvalidRefundLockTime,
                            message: $"Invalid refund time in initiated event. Expected value is {refundTimeStamp}, actual is {(long)initiatedEvent.RefundTimestamp}");
                    }

                    if (swap.IsAcceptor)
                    {
                        if (initiatedEvent.RedeemFee != requiredRewardForRedeemInWei)
                        {
                            Log.Debug(
                                "Invalid redeem fee in initiated event. Expected value is {@expected}, actual is {@actual}",
                                requiredRewardForRedeemInWei,
                                (long)initiatedEvent.RedeemFee);

                            return new Error(
                                code: Errors.InvalidRewardForRedeem,
                                message: $"Invalid redeem fee in initiated event. Expected value is {requiredRewardForRedeemInWei}, actual is {(long)initiatedEvent.RedeemFee}");
                        }
                    }

                    return true;
                }

                Log.Debug(
                    "Eth value is not enough. Expected value is {@expected}. Actual value is {@actual}",
                    (decimal)(requiredAmountInWei - requiredRewardForRedeemInWei),
                    (decimal)initiatedEvent.Value);
                
                var (addEvents, addError) = await api
                    .GetContractEventsAsync(
                        address: ethereumConfig.SwapContractAddress,
                        fromBlock: ethereumConfig.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<AddedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (addError != null)
                    return addError;

                events = addEvents;

                if (events == null || !events.Any())
                    return false;

                foreach (var @event in events.Select(e => e.ParseAddedEvent()))
                {
                    if (@event.Value >= requiredAmountInWei - requiredRewardForRedeemInWei)
                        return true;

                    Log.Debug(
                        "Eth value is not enough. Expected value is {@expected}. Actual value is {@actual}",
                        requiredAmountInWei - requiredRewardForRedeemInWei,
                        (long)@event.Value);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum swap initiated control task error");

                return new Error(Errors.InternalError, e.Message);
            }

            return false;
        }

        public static async Task StartSwapInitiatedControlAsync(
            Swap swap,
            EthereumConfig ethereumConfig,
            long refundTimeStamp,
            TimeSpan interval,
            Func<Swap, CancellationToken, Task> initiatedHandler = null,
            Func<Swap, CancellationToken, Task> canceledHandler = null,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} started", ethereumConfig.Name, swap.Id);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (swap.IsCanceled || DateTimeOffset.UtcNow >= DateTimeOffset.FromUnixTimeSeconds(refundTimeStamp))
                    {
                        await canceledHandler
                            .Invoke(swap, cancellationToken)
                            .ConfigureAwait(false);

                        break;
                    }

                    var (isInitiated, error) = await IsInitiatedAsync(
                            swap: swap,
                            ethereumConfig: ethereumConfig,
                            refundTimeStamp: refundTimeStamp,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                    {
                        Log.Error("{@currency} IsInitiatedAsync error for swap {@swap}. Code: {@code}. Message: {@desc}",
                            ethereumConfig.Name,
                            swap.Id,
                            error.Value.Code,
                            error.Value.Message);
                    }
                    else if (error == null && isInitiated)
                    {
                        await initiatedHandler
                            .Invoke(swap, cancellationToken)
                            .ConfigureAwait(false);

                        break;
                    }

                    await Task.Delay(interval, cancellationToken)
                        .ConfigureAwait(false);
                }

                Log.Debug("StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} {@message}",
                    ethereumConfig.Name,
                    swap.Id,
                    cancellationToken.IsCancellationRequested ? "canceled" : "completed");
            }
            catch (OperationCanceledException)
            {
                Log.Debug("StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} canceled",
                    ethereumConfig.Name,
                    swap.Id);
            }
            catch (Exception e)
            {
                Log.Error(e, "StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} error",
                    ethereumConfig.Name,
                    swap.Id);
            }
        }
    }
}