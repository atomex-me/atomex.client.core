using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Swaps.Tezos.FA2.Helpers
{
    public static class FA2SwapInitiatedHelper
    {
        public static async Task<Result<IBlockchainTransaction>> TryToFindPaymentAsync(
            Swap swap,
            Currency currency,
            CancellationToken cancellationToken = default)
        {
            var fa2 = currency as TezosTokens.FA2;

            if (!(swap.PaymentTx is TezosTransaction savedTx))
                return new Error(Errors.SwapError, "Saved tx is null");

            var savedParameters = savedTx.Params?.ToString(Newtonsoft.Json.Formatting.None);

            if (savedParameters == null)
                return new Error(Errors.SwapError, "Saved tx has null parameters");

            var api = fa2.BlockchainApi as ITezosBlockchainApi;

            var txsResult = await api
                .TryGetTransactionsAsync(
                    from: savedTx.From,
                    to: fa2.SwapContractAddress,
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
            Currency currency,
            Atomex.Tezos tezos,
            long refundTimeStamp,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Tezos FA2: check initiated event");

                var fa2 = (TezosTokens.FA2)currency;

                var side = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var requiredAmountInTokenDigits = AmountHelper
                    .QtyToAmount(side, swap.Qty, swap.Price, fa2.DigitsMultiplier)
                    .ToTokenDigits(fa2.DigitsMultiplier);

                var contractAddress = fa2.SwapContractAddress;
                var detectedAmountInTokenDigits = 0m;

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

                        if (IsSwapInit(tx, fa2.TokenContractAddress, fa2.TokenID, swap.SecretHash, swap.ToAddress))
                        {
                            // init payment to secret hash!
                            detectedPayment = true;
                            detectedAmountInTokenDigits += GetAmount(tx);
                            detectedRefundTimestamp = GetRefundTimestamp(tx);
                        }

                        if (detectedPayment && detectedAmountInTokenDigits >= requiredAmountInTokenDigits)
                        {
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
            Currency currency,
            Atomex.Tezos tezos,
            long refundTimeStamp,
            TimeSpan interval,
            Func<Swap, CancellationToken, Task> initiatedHandler,
            Func<Swap, CancellationToken, Task> canceledHandler,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (swap.IsCanceled)
                    {
                        await canceledHandler.Invoke(swap, cancellationToken)
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

                    if (isInitiatedResult.HasError && isInitiatedResult.Error.Code != Errors.RequestError)
                    {
                        await canceledHandler.Invoke(swap, cancellationToken)
                            .ConfigureAwait(false);

                        break;
                    }
                    else if (!isInitiatedResult.HasError && isInitiatedResult.Value)
                    {
                        await initiatedHandler.Invoke(swap, cancellationToken)
                            .ConfigureAwait(false);

                        break;
                    }

                    await Task.Delay(interval, cancellationToken)
                        .ConfigureAwait(false);
                }
            }, cancellationToken);
        }

        public static bool IsSwapInit(
            TezosTransaction tx,
            string tokenContractAddress,
            long tokenID,
            byte[] secretHash,
            string participant)
        {
            try
            {
                return tx.Params["entrypoint"].ToString().Equals("initiate") &&
                       tx.Params["value"]["args"][0]["args"][0]["args"][0]["bytes"].ToString().Equals(secretHash.ToHexString()) &&
                       tx.Params["value"]["args"][0]["args"][0]["args"][1]["string"].ToString().Equals(participant) &&
                       tx.Params["value"]["args"][0]["args"][1]["args"][1]["string"].ToString().Equals(tokenContractAddress) &&
                       tx.Params["value"]["args"][1]["args"][0]["int"].ToObject<long>().Equals(tokenID);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static decimal GetAmount(TezosTransaction tx)
        {
            return tx.Params?["value"]?["args"]?[1]?["args"]?[1]?["int"]?.ToObject<decimal>() ?? 0;
        }

        public static long GetRefundTimestamp(TezosTransaction tx)
        {
            return tx.Params?["value"]?["args"]?[0]?["args"][1]["args"]?[0]?["string"]?.ToObject<long>() ?? 0;
        }
    }
}