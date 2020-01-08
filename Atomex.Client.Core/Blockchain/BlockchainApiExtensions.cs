using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain
{
    public static class BlockchainApiExtensions
    {
        public static async Task<Result<IBlockchainTransaction>> TryGetTransactionAsync(
            this IBlockchainApi api,
            string txId,
            int attempts = 10,
            int attemptIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            var attempt = 0;

            while (attempt < attempts)
            {
                ++attempt;

                var txResult = await api
                    .GetTransactionAsync(txId, cancellationToken)
                    .ConfigureAwait(false);

                if (txResult == null || txResult.IsConnectionError)
                {
                    await Task.Delay(attemptIntervalMs)
                        .ConfigureAwait(false);
                }
                else return txResult;
            }

            return new Error(Errors.RequestError, $"Connection error while getting transaciton after {attempts} attempts");
        }
    }
}
