﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Serilog;

namespace Atomex.Swaps.Tezos.Helpers
{
    public static class TezosSwapRedeemedHelper
    {
        public static async Task<Result<byte[]>> IsRedeemedAsync(
            Swap swap,
            Currency currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Tezos: check redeem event");

                var tezos = (Atomex.Tezos)currency;

                var contractAddress = tezos.SwapContractAddress;

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

                if (txs != null)
                {
                    foreach (var tx in txs)
                    {
                        if (tx.To == contractAddress && IsSwapRedeem(tx, swap.SecretHash))
                        {
                            // redeem!
                            var secret = GetSecret(tx);

                            Log.Debug("Redeem event received with secret {@secret}", Convert.ToBase64String(secret));

                            return secret;
                        }

                        if (tx.BlockInfo?.BlockTime == null)
                            continue;

                        var blockTimeUtc = tx.BlockInfo.BlockTime.Value.ToUniversalTime();
                        var swapTimeUtc = swap.TimeStamp.ToUniversalTime();

                        if (blockTimeUtc < swapTimeUtc)
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("Tezos redeem control task error");

                return new Error(Errors.InternalError, e.Message);
            }

            return (byte[])null;
        }

        public static async Task<Result<byte[]>> IsRedeemedAsync(
            Swap swap,
            Currency currency,
            int attempts,
            int attemptIntervalInSec,
            CancellationToken cancellationToken = default)
        {
            var attempt = 0;

            while (!cancellationToken.IsCancellationRequested && attempt < attempts)
            {
                ++attempt;

                var isRedeemedResult = await IsRedeemedAsync(
                        swap: swap,
                        currency: currency,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (isRedeemedResult.HasError && isRedeemedResult.Error.Code != Errors.RequestError) // has error
                    return isRedeemedResult;
  
                if (!isRedeemedResult.HasError)
                    return isRedeemedResult;

                await Task.Delay(TimeSpan.FromSeconds(attemptIntervalInSec), cancellationToken)
                    .ConfigureAwait(false);
            }

            return new Error(Errors.MaxAttemptsCountReached, "Max attempts count reached for redeem check");
        }

        public static Task StartSwapRedeemedControlAsync(
            Swap swap,
            Currency currency,
            DateTime refundTimeUtc,
            TimeSpan interval,
            bool cancelOnlyIfRefundTimeReached,
            Func<Swap, byte[], CancellationToken, Task> redeemedHandler,
            Func<Swap, DateTime, CancellationToken, Task> canceledHandler,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var isRedeemedResult = await IsRedeemedAsync(
                            swap: swap,
                            currency: currency,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (isRedeemedResult.HasError && isRedeemedResult.Error.Code != Errors.RequestError) // has error
                    {
                        await canceledHandler
                            .Invoke(swap, refundTimeUtc, cancellationToken)
                            .ConfigureAwait(false);

                        break;
                    }
                    else if (!isRedeemedResult.HasError && isRedeemedResult.Value != null) // has secret
                    {
                        await redeemedHandler
                            .Invoke(swap, isRedeemedResult.Value, cancellationToken)
                            .ConfigureAwait(false);

                        break;
                    }

                    if (!cancelOnlyIfRefundTimeReached || DateTime.UtcNow >= refundTimeUtc)
                    {
                        await canceledHandler
                            .Invoke(swap, refundTimeUtc, cancellationToken)
                            .ConfigureAwait(false);

                        break;
                    }

                    await Task.Delay(interval, cancellationToken)
                        .ConfigureAwait(false);
                }
            }, cancellationToken);
        }

        public static bool IsSwapRedeem(TezosTransaction tx, byte[] secretHash)
        {
            try
            {
                var secretBytes = Hex.FromString(tx.Params["value"]["args"][0]["args"][0]["bytes"].ToString());
                var secretHashBytes = CurrencySwap.CreateSwapSecretHash(secretBytes);

                return secretHashBytes.SequenceEqual(secretHash);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static byte[] GetSecret(TezosTransaction tx)
        {
            return Hex.FromString(tx.Params["value"]["args"][0]["args"][0]["bytes"].ToString());
        }
    }
}