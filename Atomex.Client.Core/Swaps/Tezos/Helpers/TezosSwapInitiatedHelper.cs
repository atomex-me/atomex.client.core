using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

using Newtonsoft.Json.Linq;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Swaps.Tezos.Helpers
{
    public static class TezosSwapInitiatedHelper
    {
        public static async Task<Result<IBlockchainTransaction>> TryToFindPaymentAsync(
            Swap swap,
            CurrencyConfig currency,
            CancellationToken cancellationToken = default)
        {
            var tezos = currency as TezosConfig;

            if (!(swap.PaymentTx is TezosTransaction savedTx))
                return new Error(Errors.SwapError, "Saved tx is null");

            var savedParameters = savedTx.Params?.ToString(Newtonsoft.Json.Formatting.None);

            if (savedParameters == null)
                return new Error(Errors.SwapError, "Saved tx has null parameters");

            var api = tezos.BlockchainApi as ITezosBlockchainApi;

            var txsResult = await api
                .TryGetTransactionsAsync(
                    from: savedTx.From,
                    to: tezos.SwapContractAddress,
                    parameters: savedParameters,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsResult == null)
                return new Error(Errors.RequestError, "Can't get Tezos swap contract transactions");

            if (txsResult.HasError)
                return txsResult.Error;

            foreach (var tx in txsResult.Value)
                if (tx.State != BlockchainTransactionState.Failed)
                    return tx;

            return new Result<IBlockchainTransaction>((IBlockchainTransaction)null);
        }

        public static async Task<Result<bool>> IsInitiatedAsync(
            Swap swap,
            CurrencyConfig currency,
            long refundTimeStamp,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Tezos: check initiated event");

                var tezos = (TezosConfig)currency;

                var side = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var requiredAmountInMtz = AmountHelper
                    .QtyToSellAmount(side, swap.Qty, swap.Price, tezos.DigitsMultiplier)
                    .ToMicroTez();

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

                        if (IsSwapInit(tx, refundTimeStamp, swap.SecretHash, swap.ToAddress))
                        {
                            // init payment to secret hash!
                            detectedPayment = true;
                            detectedAmountInMtz += tx.Amount;
                            detectedRedeemFeeAmountInMtz = GetRedeemFee(tx);
                        }
                        else if (IsSwapAdd(tx, swap.SecretHash))
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
            Swap swap,
            CurrencyConfig currency,
            long refundTimeStamp,
            TimeSpan interval,
            Func<Swap, CancellationToken, Task> initiatedHandler,
            Func<Swap, CancellationToken, Task> canceledHandler,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (swap.IsCanceled)
                        {
                            await canceledHandler
                                .Invoke(swap, cancellationToken)
                                .ConfigureAwait(false);

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
                            await canceledHandler
                                .Invoke(swap, cancellationToken)
                                .ConfigureAwait(false);

                            break;
                        }
                        else if (!isInitiatedResult.HasError && isInitiatedResult.Value)
                        {
                            await initiatedHandler
                                .Invoke(swap, cancellationToken)
                                .ConfigureAwait(false);

                            break;
                        }

                        await Task.Delay(interval, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("StartSwapInitiatedControlAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "StartSwapInitiatedControlAsync error.");
                }

            }, cancellationToken);
        }

        public static bool IsSwapInit(TezosTransaction tx, long refundTimestamp, byte[] secretHash, string participant)
        {
            try
            {
                if (tx.Params == null)
                    return false;

                var entrypoint = tx.Params?["entrypoint"]?.ToString();

                return entrypoint switch
                {
                    "default"  => IsSwapInit(tx.Params?["value"]?["args"]?[0]?["args"]?[0], secretHash.ToHexString(), participant, refundTimestamp),
                    "fund"     => IsSwapInit(tx.Params?["value"]?["args"]?[0], secretHash.ToHexString(), participant, refundTimestamp),
                    "initiate" => IsSwapInit(tx.Params?["value"], secretHash.ToHexString(), participant, refundTimestamp),
                    _          => false
                };
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsSwapInit(
            JToken initParams,
            string secretHash,
            string participantAddress,
            long refundTimeStamp)
        {
            if (initParams?["args"]?[1]?["args"]?[0]?["args"]?[0]?["bytes"]?.Value<string>() != secretHash)
                return false;

            try
            {
                var timestamp = TezosConfig.ParseTimestamp(initParams?["args"]?[1]?["args"]?[0]?["args"]?[1]);
                if (timestamp < refundTimeStamp)
                {
                    Log.Debug($"IsSwapInit: refundTimeStamp is less than expected (should be at least {refundTimeStamp})");
                    return false;
                }

                var address = TezosConfig.ParseAddress(initParams?["args"]?[0]);
                if (address != participantAddress)
                {
                    Log.Debug($"IsSwapInit: participantAddress is unexpected (should be {participantAddress})");
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Error($"IsSwapInit: {e.Message}");
                return false;
            }

            return true;
        }

        public static bool IsSwapAdd(TezosTransaction tx, byte[] secretHash)
        {
            try
            {
                if (tx.Params == null)
                    return false;

                var entrypoint = tx.Params?["entrypoint"]?.ToString();

                return entrypoint switch
                {
                    "default" => IsSwapAdd(tx.Params?["value"]?["args"]?[0]?["args"]?[0], secretHash.ToHexString()) && tx.Params?["value"]?["prim"]?.Value<string>() == "Left",
                    "fund"    => IsSwapAdd(tx.Params?["value"]?["args"]?[0], secretHash.ToHexString()),
                    "add"     => IsSwapAdd(tx.Params?["value"], secretHash.ToHexString()),
                    _         => false
                };
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsSwapAdd(JToken addParams, string secretHash)
        {
            return addParams?["bytes"]?.Value<string>() == secretHash;
        }

        public static decimal GetRedeemFee(TezosTransaction tx)
        {
            var entrypoint = tx.Params?["entrypoint"]?.ToString();

            return entrypoint switch
            {
                "default"  => GetRedeemFee(tx.Params?["value"]?["args"]?[0]?["args"]?[0]),
                "fund"     => GetRedeemFee(tx.Params?["value"]?["args"]?[0]),
                "initiate" => GetRedeemFee(tx.Params?["value"]),
                _          => 0
            };
        }

        private static decimal GetRedeemFee(JToken initiateParams)
        {
            return decimal.Parse(initiateParams["args"][1]["args"][1]["int"].ToString());
        }
    }
}