using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.TezosTokens;
using Atomex.Swaps.Abstract;

namespace Atomex.Swaps.Tezos.Fa2.Helpers
{
    public static class Fa2SwapInitiatedHelper
    {
        public static async Task<Result<ITransaction>> TryToFindPaymentAsync(
            Swap swap,
            CurrencyConfig currency,
            CancellationToken cancellationToken = default)
        {
            var fa2 = currency as Fa2Config;

            if (swap.PaymentTx is not TezosTransaction paymentTx)
                throw new ArgumentNullException("Swap payment transaction is null");

            var lockTimeInSeconds = swap.IsInitiator
                ? CurrencySwap.DefaultInitiatorLockTimeInSeconds
                : CurrencySwap.DefaultAcceptorLockTimeInSeconds;

            var refundTime = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds))
                .ToString("yyyy-MM-ddTHH:mm:ssZ");

            var rewardForRedeemInTokenDigits = swap.IsInitiator
                ? swap.PartyRewardForRedeem.ToTokenDigits(fa2.DigitsMultiplier)
                : 0;

            var requiredAmountInTokens = Fa2Swap.RequiredAmountInTokens(swap, fa2);

            var parameters = "entrypoint=initiate" +
                $"&parameter.hashedSecret={swap.SecretHash.ToHexString()}" +
                $"&parameter.participant={swap.PartyAddress}" +
                $"&parameter.refundTime={refundTime}" +
                $"&parameter.tokenAddress={fa2.TokenContractAddress}" +
                $"&parameter.tokenId={fa2.TokenId}" +
                $"&parameter.totalAmount={requiredAmountInTokens.ToTokenDigits(fa2.DigitsMultiplier)}" +
                $"&parameter.payoffAmount={(long)rewardForRedeemInTokenDigits}";

            var api = fa2.BlockchainApi as ITezosBlockchainApi;

            var txsResult = await api
                .TryGetTransactionsAsync(
                    from: paymentTx.From,
                    to: fa2.SwapContractAddress,
                    parameters: parameters,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsResult == null)
                return new Error(Errors.RequestError, "Can't get Tezos swap contract transactions");

            if (txsResult.HasError)
                return txsResult.Error;

            foreach (var tx in txsResult.Value)
                if (tx.Status != TransactionStatus.Failed)
                    return tx;

            return new Result<ITransaction>((ITransaction)null);
        }

        public static async Task<Result<bool>> IsInitiatedAsync(
            Swap swap,
            CurrencyConfig currency,
            TezosConfig tezos,
            long refundTimeStamp,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Tezos FA2: check initiated event");

                var fa2 = (Fa2Config)currency;

                var side = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var requiredAmountInTokenDigits = AmountHelper
                    .QtyToSellAmount(side, swap.Qty, swap.Price, fa2.DigitsMultiplier)
                    .ToTokenDigits(fa2.DigitsMultiplier);

                var requiredRewardForRedeemInTokenDigits = swap.IsAcceptor
                    ? swap.RewardForRedeem.ToTokenDigits(fa2.DigitsMultiplier)
                    : 0;

                var contractAddress = fa2.SwapContractAddress;
                var detectedAmountInTokenDigits = 0m;
                var detectedRedeemFeeAmountInTokenDigits = 0m;

                long detectedRefundTimestamp = 0;

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

                        if (IsSwapInit(tx, swap.SecretHash.ToHexString(), fa2.TokenContractAddress, swap.ToAddress, refundTimeStamp, fa2.TokenId))
                        {
                            // init payment to secret hash!
                            detectedPayment = true;
                            detectedAmountInTokenDigits += GetAmount(tx);
                            detectedRedeemFeeAmountInTokenDigits = GetRedeemFee(tx);
                            detectedRefundTimestamp = GetRefundTimestamp(tx);
                        }

                        if (detectedPayment && detectedAmountInTokenDigits >= requiredAmountInTokenDigits)
                        {
                            if (swap.IsAcceptor && detectedRedeemFeeAmountInTokenDigits != requiredRewardForRedeemInTokenDigits)
                            {
                                Log.Debug(
                                    "Invalid redeem fee in initiated event. Expected value is {@expected}, actual is {@actual}",
                                    requiredRewardForRedeemInTokenDigits,
                                    detectedRedeemFeeAmountInTokenDigits);

                                return new Error(
                                    code: Errors.InvalidRewardForRedeem,
                                    description: $"Invalid redeem fee in initiated event. Expected value is {requiredRewardForRedeemInTokenDigits}, actual is {detectedRedeemFeeAmountInTokenDigits}");
                            }

                            if (detectedRefundTimestamp < refundTimeStamp)
                            {
                                Log.Debug(
                                    "Invalid refund timestamp in initiated event. Expected value is {@expected}, actual is {@actual}",
                                    refundTimeStamp,
                                    detectedRefundTimestamp);

                                return new Error(
                                    code: Errors.InvalidRewardForRedeem,
                                    description: $"Invalid refund timestamp in initiated event. Expected value is {refundTimeStamp}, actual is {detectedRefundTimestamp}");
                            }

                            return true; // todo: check also token contract transfers
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
                Log.Error(e, "Tezos token swap initiated control task error");

                return new Error(Errors.InternalError, e.Message);
            }

            return false;
        }

        public static Task StartSwapInitiatedControlAsync(
            Swap swap,
            CurrencyConfig currency,
            TezosConfig tezos,
            long refundTimeStamp,
            TimeSpan interval,
            Func<Swap, CancellationToken, Task> initiatedHandler,
            Func<Swap, CancellationToken, Task> canceledHandler,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} started", currency.Name, swap.Id);

            return Task.Run(async () =>
            {
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

                        var isInitiatedResult = await IsInitiatedAsync(
                                swap: swap,
                                currency: currency,
                                tezos: tezos,
                                refundTimeStamp: refundTimeStamp,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (isInitiatedResult.HasError)
                            Log.Error("{@currency} IsInitiatedAsync error for swap {@swap}. Code: {@code}. Description: {@desc}",
                                currency.Name,
                                swap.Id,
                                isInitiatedResult.Error.Code,
                                isInitiatedResult.Error.Description);

                        if (!isInitiatedResult.HasError && isInitiatedResult.Value)
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
                        currency.Name,
                        swap.Id,
                        cancellationToken.IsCancellationRequested ? "canceled" : "completed");
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} canceled",
                        currency.Name,
                        swap.Id);
                }
                catch (Exception e)
                {
                    Log.Error(e, "StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} error",
                        currency.Name,
                        swap.Id);
                }

            }, cancellationToken);
        }

        public static bool IsSwapInit(
            TezosTransaction tx,
            string secretHash,
            string tokenContractAddress,
            string participant,
            long refundTimeStamp,
            long tokenId)
        {
            try
            {
                if (tx.Params == null)
                    return false;

                var entrypoint = tx.Params?["entrypoint"]?.ToString();

                return entrypoint switch
                {
                    "default" => IsSwapInit(
                        tx.Params?["value"]?["args"]?[0]?["args"]?[0],
                        secretHash,
                        tokenContractAddress,
                        participant,
                        refundTimeStamp,
                        tokenId),
                    "initiate" => IsSwapInit(
                        tx.Params?["value"],
                        secretHash,
                        tokenContractAddress,
                        participant,
                        refundTimeStamp,
                        tokenId),
                    _ => false
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
            string tokenContractAddress,
            string participantAddress,
            long refundTimeStamp,
            long tokenId)
        {
            if (initParams?["args"]?[0]?["args"]?[0]?["args"]?[0]?["bytes"]?.Value<string>() != secretHash)
                return false;

            try
            {
                var timestamp = TezosConfig.ParseTimestamp(initParams?["args"]?[0]?["args"]?[1]?["args"]?[1]);
                if (timestamp < refundTimeStamp)
                {
                    Log.Debug($"IsSwapInit: refundTimeStamp is less than expected (should be at least {refundTimeStamp})");
                    return false;
                }

                var address = TezosConfig.ParseAddress(initParams?["args"]?[0]?["args"]?[0]?["args"]?[1]);
                if (address != participantAddress)
                {
                    Log.Debug($"IsSwapInit: participantAddress is unexpected (should be {participantAddress})");
                    return false;
                }

                var tokenAddress = TezosConfig.ParseAddress(initParams?["args"]?[1]?["args"]?[0]?["args"]?[0]);
                if (tokenAddress != tokenContractAddress)
                {
                    Log.Debug($"IsSwapInit: tokenContractAddress is unexpected (should be {tokenContractAddress})");
                    return false;
                }

                if (initParams?["args"]?[1]?["args"]?[0]?["args"]?[1]?["int"]?.Value<int>() != tokenId)
                {
                    Log.Debug($"IsSwapInit: tokenId is unexpected (should be {tokenId})");
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

        public static decimal GetAmount(TezosTransaction tx)
        {
            if (tx.Params == null)
                return 0m;

            var entrypoint = tx.Params?["entrypoint"]?.ToString();

            return entrypoint switch
            {
                "default" => GetAmount(tx.Params?["value"]?["args"]?[0]?["args"]?[0]),
                "initiate" => GetAmount(tx.Params?["value"]),
                _ => 0m
            };
        }

        private static decimal GetAmount(JToken initParams)
        {
            return initParams?["args"]?[1]?["args"]?[1]?["int"]?.ToObject<decimal>() ?? 0m;
        }

        public static long GetRefundTimestamp(TezosTransaction tx)
        {
            if (tx.Params == null)
                return 0;

            var entrypoint = tx.Params?["entrypoint"]?.ToString();

            return entrypoint switch
            {
                "default" => GetRefundTimestamp(tx.Params?["value"]?["args"]?[0]?["args"]?[0]),
                "initiate" => GetRefundTimestamp(tx.Params?["value"]),
                _ => 0
            };
        }

        private static long GetRefundTimestamp(JToken initParams)
        {
            return TezosConfig.ParseTimestamp(initParams?["args"]?[0]?["args"]?[1]?["args"]?[1]);
        }

        public static decimal GetRedeemFee(TezosTransaction tx)
        {
            if (tx.Params == null)
                return 0;

            var entrypoint = tx.Params?["entrypoint"]?.ToString();

            return entrypoint switch
            {
                "default" => GetRedeemFee(tx.Params?["value"]?["args"]?[0]?["args"]?[0]),
                "initiate" => GetRedeemFee(tx.Params?["value"]),
                _ => 0
            };
        }

        public static decimal GetRedeemFee(JToken initParams)
        {
            return decimal.Parse(initParams["args"]?[0]?["args"]?[1]?["args"]?[0]?["int"]?.ToString());
        }
    }
}