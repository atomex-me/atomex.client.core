using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Atomex.Core;
using Serilog;

namespace Atomex.Swaps.Ethereum.Helpers
{
    public static class EthereumSwapInitiatedHelper
    {
        public static async Task<Result<bool>> IsInitiatedAsync(
            Swap swap,
            Currency currency,
            long refundTimeStamp,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum: check initiated event");

                var ethereum = (Atomex.Ethereum)currency;

                var sideOpposite = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var requiredAmountInEth = AmountHelper.QtyToAmount(sideOpposite, swap.Qty, swap.Price, ethereum.DigitsMultiplier);
                var requiredAmountInWei = Atomex.Ethereum.EthToWei(requiredAmountInEth);
                var requiredRewardForRedeemInWei = Atomex.Ethereum.EthToWei(swap.RewardForRedeem);


                var api = new EtherScanApi(ethereum);

                var initiateEventsResult = await api
                    .GetContractEventsAsync(
                        address: ethereum.SwapContractAddress,
                        fromBlock: ethereum.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<InitiatedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        topic2: "0x000000000000000000000000" + swap.ToAddress.Substring(2),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (initiateEventsResult == null)
                    return new Error(Errors.RequestError, $"Connection error while getting contract {ethereum.SwapContractAddress} initiate event");

                if (initiateEventsResult.HasError)
                    return initiateEventsResult.Error;

                var events = initiateEventsResult.Value?.ToList();

                if (events == null || !events.Any())
                    return false;

                var initiatedEvent = events.First().ParseInitiatedEvent();

                if (initiatedEvent.Value >= requiredAmountInWei - requiredRewardForRedeemInWei)
                {
                    if (initiatedEvent.RefundTimestamp != refundTimeStamp)
                    {
                        Log.Debug(
                            "Invalid refund time in initiated event. Expected value is {@expected}, actual is {@actual}",
                            refundTimeStamp,
                            (long)initiatedEvent.RefundTimestamp);

                        return new Error(
                            code: Errors.InvalidRefundLockTime,
                            description: $"Invalid refund time in initiated event. Expected value is {refundTimeStamp}, actual is {(long)initiatedEvent.RefundTimestamp}");
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
                                description: $"Invalid redeem fee in initiated event. Expected value is {requiredRewardForRedeemInWei}, actual is {(long)initiatedEvent.RedeemFee}");
                        }
                    }

                    return true;
                }

                Log.Debug(
                    "Eth value is not enough. Expected value is {@expected}. Actual value is {@actual}",
                    (decimal)(requiredAmountInWei - requiredRewardForRedeemInWei),
                    (decimal)initiatedEvent.Value);
                
                var addEventsResult = await api
                    .GetContractEventsAsync(
                        address: ethereum.SwapContractAddress,
                        fromBlock: ethereum.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<AddedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (addEventsResult == null)
                    return new Error(Errors.RequestError, $"Connection error while getting contract {ethereum.SwapContractAddress} add event");

                if (addEventsResult.HasError)
                    return addEventsResult.Error;

                events = addEventsResult.Value?.ToList();

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
                Log.Error("Ethereum swap initiated control task error");

                return new Error(Errors.InternalError, e.Message);
            }

            return false;
        }

        public static Task StartSwapInitiatedControlAsync(
            Swap swap,
            Currency currency,
            long refundTimeStamp,
            TimeSpan interval,
            Action<Swap, CancellationToken> initiatedHandler = null,
            Action<Swap, CancellationToken> canceledHandler = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (swap.IsCanceled) {
                        canceledHandler?.Invoke(swap, cancellationToken);
                        break;
                    }

                    var isInitiatedResult = await IsInitiatedAsync(
                            swap: swap,
                            currency: currency,
                            refundTimeStamp: refundTimeStamp,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (isInitiatedResult.HasError && isInitiatedResult.Error.Code != Errors.RequestError)
                    {
                        canceledHandler?.Invoke(swap, cancellationToken);
                        break;
                    }
                    else if (!isInitiatedResult.HasError && isInitiatedResult.Value)
                    {
                        initiatedHandler?.Invoke(swap, cancellationToken);
                        break;
                    }

                    await Task.Delay(interval, cancellationToken)
                        .ConfigureAwait(false);
                }
            }, cancellationToken);
        }
    }
}