using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Serilog;

namespace Atomex.Swaps.Ethereum.Helpers
{
    public static class EthereumSwapInitiatedHelper
    {
        public static async Task<Result<bool>> IsInitiatedAsync(
            ClientSwap swap,
            Currency currency,
            long refundTimeStamp,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum: check initiated event");

                var ethereum = (Atomex.Ethereum)currency;

                var side = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var requiredAmountInEth = AmountHelper.QtyToAmount(side, swap.Qty, swap.Price);
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
                    return new Result<bool>(new Error(Errors.RequestError, $"Connection error while trying to get contract {ethereum.SwapContractAddress} initiate event"));

                if (initiateEventsResult.HasError)
                    return new Result<bool>(initiateEventsResult.Error);

                var events = initiateEventsResult.Value?.ToList();

                if (events == null || !events.Any())
                    return new Result<bool>(false);

                var initiatedEvent = events.First().ParseInitiatedEvent();

                if (initiatedEvent.Value >= requiredAmountInWei - requiredRewardForRedeemInWei)
                {
                    if (swap.IsAcceptor)
                    {
                        if (initiatedEvent.RedeemFee != requiredRewardForRedeemInWei)
                        {
                            Log.Debug(
                                "Invalid redeem fee in initiated event. Expected value is {@expected}, actual is {@actual}",
                                requiredRewardForRedeemInWei,
                                (long)initiatedEvent.RedeemFee);

                            return new Result<bool>(
                                new Error(
                                    code: Errors.InvalidRewardForRedeem,
                                    description: $"Invalid redeem fee in initiated event. Expected value is {requiredRewardForRedeemInWei}, actual is {(long)initiatedEvent.RedeemFee}"));
                        }

                        if (initiatedEvent.RefundTimestamp != refundTimeStamp)
                        {
                            Log.Debug(
                                "Invalid refund time in initiated event. Expected value is {@expected}, actual is {@actual}",
                                refundTimeStamp,
                                (long)initiatedEvent.RefundTimestamp);

                            return new Result<bool>(
                                new Error(
                                    code: Errors.InvalidRefundLockTime,
                                    description: $"Invalid refund time in initiated event. Expected value is {refundTimeStamp}, actual is {(long)initiatedEvent.RefundTimestamp}"));
                        }
                    }

                    return new Result<bool>(true);
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
                    return new Result<bool>(new Error(Errors.RequestError, $"Connection error while trying to get contract {ethereum.SwapContractAddress} add event"));

                if (addEventsResult.HasError)
                    return new Result<bool>(addEventsResult.Error);

                events = addEventsResult.Value?.ToList();

                if (events == null || !events.Any())
                    return new Result<bool>(false);

                foreach (var @event in events.Select(e => e.ParseAddedEvent()))
                {
                    if (@event.Value >= requiredAmountInWei - requiredRewardForRedeemInWei)
                        return new Result<bool>(true);

                    Log.Debug(
                        "Eth value is not enough. Expected value is {@expected}. Actual value is {@actual}",
                        requiredAmountInWei - requiredRewardForRedeemInWei,
                        (long)@event.Value);
                }
                
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum swap initiated control task error");

                return new Result<bool>(new Error(Errors.InternalError, e.Message));
            }

            return new Result<bool>(false);
        }

        public static Task StartSwapInitiatedControlAsync(
            ClientSwap swap,
            Currency currency,
            long refundTimeStamp,
            TimeSpan interval,
            Action<ClientSwap, CancellationToken> initiatedHandler = null,
            Action<ClientSwap, CancellationToken> canceledHandler = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var isInitiatedResult = await IsInitiatedAsync(
                            swap: swap,
                            currency: currency,
                            refundTimeStamp: refundTimeStamp,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (isInitiatedResult.HasError)
                    {
                        if (isInitiatedResult.Error.Code != Errors.RequestError)
                        {
                            canceledHandler?.Invoke(swap, cancellationToken);
                            break;
                        }
                    }
                    else if (isInitiatedResult.Value)
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