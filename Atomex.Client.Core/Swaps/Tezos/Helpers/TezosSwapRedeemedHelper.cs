using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Serilog;

namespace Atomex.Swaps.Tezos.Helpers
{
    public static class TezosSwapRedeemedHelper
    {
        public static async Task<Result<byte[]>> IsRedeemedAsync(
            ClientSwap swap,
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
                    .GetTransactionsAsync(contractAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (txsResult.HasError)
                {
                    Log.Error("Error while get transactions from contract {@contract}. Code: {@code}. Description: {@desc}",
                        contractAddress,
                        txsResult.Error.Code,
                        txsResult.Error.Description);

                    return new Result<byte[]>(txsResult.Error);
                }

                var txs = txsResult.Value
                    ?.Cast<TezosTransaction>()
                    .ToList();

                if (txs != null)
                {
                    foreach (var tx in txs)
                    {
                        if (tx.To == contractAddress && tx.IsSwapRedeem(swap.SecretHash))
                        {
                            // redeem!
                            var secret = tx.GetSecret();

                            Log.Debug("Redeem event received with secret {@secret}", Convert.ToBase64String(secret));

                            return new Result<byte[]>(secret);
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
                Log.Error(e, "Tezos redeem control task error");

                return new Result<byte[]>(new Error(Errors.InternalError, e.Message));
            }

            return new Result<byte[]>((byte[])null);
        }

        public static Task StartSwapRedeemedControlAsync(
            ClientSwap swap,
            Currency currency,
            DateTime refundTimeUtc,
            TimeSpan interval,
            bool cancelOnlyIfRefundTimeReached = true,
            Action<ClientSwap, byte[], CancellationToken> redeemedHandler = null,
            Action<ClientSwap, DateTime, CancellationToken> canceledHandler = null,
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

                    if (isRedeemedResult.HasError) // has error
                    {
                        canceledHandler?.Invoke(swap, refundTimeUtc, cancellationToken);
                        break;
                    }

                    if (isRedeemedResult.Value != null) // has secret
                    {
                        redeemedHandler?.Invoke(swap, isRedeemedResult.Value, cancellationToken);
                        break;
                    }

                    if (!cancelOnlyIfRefundTimeReached || DateTime.UtcNow >= refundTimeUtc)
                    {
                        canceledHandler?.Invoke(swap, refundTimeUtc, cancellationToken);
                        break;
                    }

                    await Task.Delay(interval, cancellationToken)
                        .ConfigureAwait(false);
                }
            }, cancellationToken);
        }
    }
}