using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Blockchain.Tezos.Tzkt.Swaps.V1;
using Atomex.Common;
using Atomex.Core;
using Atomex.TezosTokens;

namespace Atomex.Swaps.Tezos.Fa12.Helpers
{
    public static class Fa12SwapRefundedHelper
    {
        public static async Task<Result<bool>> IsRefundedAsync(
            Swap swap,
            CurrencyConfig currency,
            TezosConfig tezos,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Tezos FA12: check refund event");

                var fa12 = (Fa12Config)currency;

                var contractAddress = fa12.SwapContractAddress;
                var secretHash = swap.SecretHash.ToHexString();

                var api = new TzktApi(tezos.GetTzktSettings());

                var (refunds, error) = await api
                    .FindRefundsAsync(
                        secretHash: secretHash,
                        contractAddress: contractAddress,
                        fromTimeStamp: (ulong)swap.TimeStamp.ToUnixTimeSeconds(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                {
                    Log.Error("Error while get transactions from contract {@contract}. Code: {@code}. Message: {@desc}",
                        contractAddress,
                        error.Value.Code,
                        error.Value.Message);

                    return error;
                }

                return refunds.Any(op => op.IsRefund(contractAddress, secretHash));
            }
            catch (Exception e)
            {
                Log.Error(e, "Tezos FA12 refund control task error");

                return new Error(Errors.InternalError, e.Message);
            }
        }

        public static async Task<Result<bool>> IsRefundedAsync(
            Swap swap,
            CurrencyConfig currency,
            TezosConfig tezos,
            int attempts,
            int attemptIntervalInSec,
            CancellationToken cancellationToken = default)
        {
            var attempt = 0;

            while (!cancellationToken.IsCancellationRequested && attempt < attempts)
            {
                ++attempt;

                var (isRefunded, error) = await IsRefundedAsync(
                        swap: swap,
                        currency: currency,
                        tezos: tezos,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null && error.Value.Code != Errors.RequestError) // has error
                    return error;

                if (error == null)
                    return isRefunded;

                await Task.Delay(TimeSpan.FromSeconds(attemptIntervalInSec), cancellationToken)
                    .ConfigureAwait(false);
            }

            return new Error(Errors.MaxAttemptsCountReached, "Max attempts count reached for refund check");
        }
    }
}