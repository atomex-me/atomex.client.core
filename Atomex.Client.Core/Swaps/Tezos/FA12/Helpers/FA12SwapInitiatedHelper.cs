using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.TezosTokens;
using Atomex.Swaps.Abstract;
using Newtonsoft.Json.Linq;

namespace Atomex.Swaps.Tezos.FA12.Helpers
{
    public static class Fa12SwapInitiatedHelper
    {
        public static async Task<Result<IBlockchainTransaction_OLD>> TryToFindPaymentAsync(
            Swap swap,
            CurrencyConfig currency,
            CancellationToken cancellationToken = default)
        {
            var fa12 = currency as Fa12Config;

            if (swap.PaymentTx is not TezosTransaction_OLD paymentTx)
                throw new ArgumentNullException("Swap payment transaction is null");

            var lockTimeInSeconds = swap.IsInitiator
                ? CurrencySwap.DefaultInitiatorLockTimeInSeconds
                : CurrencySwap.DefaultAcceptorLockTimeInSeconds;

            var refundTime = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds))
                .ToString("yyyy-MM-ddTHH:mm:ssZ");

            var rewardForRedeemInTokenDigits = swap.IsInitiator
                ? swap.PartyRewardForRedeem.ToTokenDigits(fa12.DigitsMultiplier)
                : 0;

            var requiredAmountInTokens = Fa12Swap.RequiredAmountInTokens(swap, fa12);

            var parameters = "entrypoint=initiate" +
                $"&parameter.refundTime={refundTime}" +
                $"&parameter.participant={swap.PartyAddress}" +
                $"&parameter.totalAmount={requiredAmountInTokens.ToTokenDigits(fa12.DigitsMultiplier)}" +
                $"&parameter.hashedSecret={swap.SecretHash.ToHexString()}" +
                $"&parameter.payoffAmount={(long)rewardForRedeemInTokenDigits}" +
                $"&parameter.tokenAddress={fa12.TokenContractAddress}";

            var api = fa12.BlockchainApi as ITezosBlockchainApi_OLD;

            var txsResult = await api
                .TryGetTransactionsAsync(
                    from: paymentTx.From,
                    to: fa12.SwapContractAddress,
                    parameters: parameters,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsResult == null)
                return new Error(Errors.RequestError, "Can't get Tezos swap contract transactions");

            if (txsResult.HasError)
                return txsResult.Error;

            foreach (var tx in txsResult.Value)
                if (tx.State != BlockchainTransactionState.Failed)
                    return tx;

            return new Result<IBlockchainTransaction_OLD>((IBlockchainTransaction_OLD)null);
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
                Log.Debug("Tezos FA12: check initiated event");

                var fa12 = (Fa12Config)currency;

                var side = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var requiredAmountInTokenDigits = AmountHelper
                    .QtyToSellAmount(side, swap.Qty, swap.Price, fa12.DigitsMultiplier)
                    .ToTokenDigits(fa12.DigitsMultiplier);

                var requiredRewardForRedeemInTokenDigits = swap.IsAcceptor ? swap.RewardForRedeem.ToTokenDigits(fa12.DigitsMultiplier) : 0;

                var contractAddress = fa12.SwapContractAddress;
                var detectedAmountInTokenDigits = 0m;
                var detectedRedeemFeeAmountInTokenDigits = 0m;

                long detectedRefundTimestamp = 0;

                var blockchainApi = (ITezosBlockchainApi_OLD)tezos.BlockchainApi;

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
                    ?.Cast<TezosTransaction_OLD>()
                    .ToList();

                if (txs == null || !txs.Any())
                    return false;

                foreach (var tx in txs)
                {
                    if (tx.IsConfirmed && tx.To == contractAddress)
                    {
                        var detectedPayment = false;

                        if (IsSwapInit(tx, swap.SecretHash.ToHexString(), fa12.TokenContractAddress, swap.ToAddress, refundTimeStamp))
                        {
                            // init payment to secret hash!
                            detectedPayment = true;
                            detectedAmountInTokenDigits += GetAmount(tx);
                            detectedRedeemFeeAmountInTokenDigits = GetRedeemFee(tx);
                            detectedRefundTimestamp = GetRefundTimestamp(tx);
                        }
                        ///not implemented
                        //else if (IsSwapAdd(tx, swap.SecretHash))
                        //{
                        //    detectedPayment = true;
                        //    detectedAmountInMtz += tx.Amount;
                        //}

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

                            if (detectedRefundTimestamp != refundTimeStamp)
                            {
                                Log.Debug(
                                    "Invalid refund timestamp in initiated event. Expected value is {@expected}, actual is {@actual}",
                                    refundTimeStamp,
                                    detectedRefundTimestamp);

                                return new Error(
                                    code: Errors.InvalidRewardForRedeem,
                                    description: $"Invalid refund timestamp in initiated event. Expected value is {refundTimeStamp}, actual is {detectedRefundTimestamp}");
                            }

                            return true;   // todo: check also token contract transfers
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
            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (swap.IsCanceled)
                        {
                            canceledHandler?.Invoke(swap, cancellationToken);
                            break;
                        }

                        var isInitiatedResult = await IsInitiatedAsync(
                                swap: swap,
                                currency: currency,
                                tezos: tezos,
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

        public static bool IsSwapInit(
            TezosTransaction_OLD tx,
            string secretHash,
            string tokenContractAddress,
            string participant,
            long refundTimeStamp)
        {
            try
            {
                if (tx.Params == null)
                    return false;

                var entrypoint = tx.Params?["entrypoint"]?.ToString();

                return entrypoint switch
                {
                    "default" => IsSwapInit(tx.Params?["value"]?["args"]?[0]?["args"]?[0], secretHash, tokenContractAddress, participant, refundTimeStamp),
                    "initiate" => IsSwapInit(tx.Params?["value"], secretHash, tokenContractAddress, participant, refundTimeStamp),
                    _ => false
                };
            }
            catch (Exception)
            {
                return false;
            }
            //try
            //{
            //    return tx.Params["entrypoint"].ToString().Equals("initiate") &&
            //           tx.Params["value"]["args"][0]["args"][0]["args"][0]["bytes"].ToString().Equals(secretHash.ToHexString()) &&
            //           tx.Params["value"]["args"][0]["args"][0]["args"][1]["string"].ToString().Equals(participant) &&
            //           tx.Params["value"]["args"][1]["args"][0]["string"].ToString().Equals(tokenContractAddress);
            //}
            //catch (Exception)
            //{
            //    return false;
            //}
        }

        private static bool IsSwapInit(
            JToken initParams,
            string secretHash,
            string tokenContractAddress,
            string participantAddress,
            long refundTimeStamp)
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

                var tokenAddress = TezosConfig.ParseAddress(initParams?["args"]?[1]?["args"]?[0]);
                if (tokenAddress != tokenContractAddress)
                {
                    Log.Debug($"IsSwapInit: tokenContractAddress is unexpected (should be {tokenContractAddress})");
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Error($"IsSwapInit: {e.Message}");
                return false;
            }

            return true;
            //return initParams?["args"]?[0]?["args"]?[0]?["args"]?[0]?["bytes"]?.Value<string>() == secretHash &&
            //       initParams?["args"]?[0]?["args"]?[0]?["args"]?[1]?["string"]?.Value<string>() == participantAddress &&
            //       initParams?["args"]?[1]?["args"]?[0]?["string"]?.Value<string>() == tokenContractAddress &&
            //       initParams?["args"]?[0]?["args"]?[1]?["args"]?[1]?["int"]?.Value<ulong>() >= refundTimeStamp;
        }

        public static decimal GetAmount(TezosTransaction_OLD tx)
        {
            return tx.Params?["value"]?["args"]?[1]?["args"]?[1]?["int"].ToObject<decimal>() ?? 0;
        }

        public static decimal GetRedeemFee(TezosTransaction_OLD tx)
        {
            return decimal.Parse(tx.Params["value"]["args"][0]["args"][1]["args"][0]["int"].ToString());
        }

        public static long GetRefundTimestamp(TezosTransaction_OLD tx)
        {
            return tx.Params["value"]["args"][0]["args"][1]["args"][1]["int"].ToObject<long>();
        }
    }
}