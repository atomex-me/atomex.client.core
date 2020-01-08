using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Serilog;

namespace Atomex.Swaps.Tezos.Helpers
{
    public static class TezosSwapInitiatedHelper
    {
        public static async Task<Result<bool>> IsInitiatedAsync(
            ClientSwap swap,
            Currency currency,
            long refundTimeStamp,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Tezos: check initiated event");

                var tezos = (Atomex.Tezos)currency;

                var side = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var requiredAmountInTz = AmountHelper.QtyToAmount(side, swap.Qty, swap.Price);
                var requiredAmountInMtz = requiredAmountInTz.ToMicroTez();
                var requiredRewardForRedeemInMtz = swap.RewardForRedeem.ToMicroTez();

                var contractAddress = tezos.SwapContractAddress;
                var detectedAmountInMtz = 0m;
                var detectedRedeemFeeAmountInMtz = 0m;

                var blockchainApi = (ITezosBlockchainApi)tezos.BlockchainApi;

                var txsResult = await blockchainApi
                    .TryGetTransactionsAsync(contractAddress, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (txsResult == null)
                    return new Error(Errors.RequestError, $"Connection error while getting txs from contract {contractAddress}");

                if (txsResult.HasError)
                {
                    Log.Error("Error while get transactions from contract {@contract}. Code: {@code}. Description: {@desc}",
                        contractAddress,
                        txsResult.Error.Code,
                        txsResult.Error.Description);

                    return txsResult.Error;
                }

                var txs = txsResult.Value
                    ?.Cast<TezosTransaction>()
                    .ToList();

                if (txs == null || !txs.Any())
                    return false;

                foreach (var tx in txs)
                {
                    if (tx.IsConfirmed && tx.To == contractAddress)
                    {
                        var detectedPayment = false;

                        if (tx.IsSwapInit(refundTimeStamp, swap.SecretHash, swap.ToAddress))
                        {
                            // init payment to secret hash!
                            detectedPayment = true;
                            detectedAmountInMtz += tx.Amount;
                            detectedRedeemFeeAmountInMtz = tx.GetRedeemFee();
                        }
                        else if (tx.IsSwapAdd(swap.SecretHash))
                        {
                            detectedPayment = true;
                            detectedAmountInMtz += tx.Amount;
                        }

                        if (detectedPayment && detectedAmountInMtz >= requiredAmountInMtz)
                        {
                            if (swap.IsAcceptor && detectedRedeemFeeAmountInMtz != requiredRewardForRedeemInMtz)
                            {
                                Log.Debug(
                                    "Invalid redeem fee in initiated event. Expected value is {@expected}, actual is {@actual}",
                                    requiredRewardForRedeemInMtz,
                                    detectedRedeemFeeAmountInMtz);

                                return new Error(
                                    code: Errors.InvalidRewardForRedeem,
                                    description: $"Invalid redeem fee in initiated event. Expected value is {requiredRewardForRedeemInMtz}, actual is {detectedRedeemFeeAmountInMtz}");
                            }

                            return true;
                        }
                    }

                    if (tx.BlockInfo?.BlockTime == null)
                        continue;

                    var blockTimeUtc = tx.BlockInfo.BlockTime.Value.ToUniversalTime();
                    var swapTimeUtc = swap.TimeStamp.ToUniversalTime();

                    if (blockTimeUtc < swapTimeUtc)
                        return false;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Tezos swap initiated control task error");

                return new Error(Errors.InternalError, e.Message);
            }

            return false;
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