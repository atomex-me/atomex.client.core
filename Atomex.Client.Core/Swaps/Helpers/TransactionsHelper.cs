using System.Threading.Tasks;
using System.Threading;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Wallet.Abstract;

namespace Atomex.Swaps.Helpers
{
    public static class TransactionsHelper
    {
        public static async Task<Result<T>> TryFindTransaction<T>(
            string txId,
            string currency,
            ILocalStorage localStorage,
            IBlockchainApi blockchainApi,
            CancellationToken cancellationToken) where T : ITransaction
        {
            var tx = await localStorage
                .GetTransactionByIdAsync<T>(currency, txId)
                .ConfigureAwait(false);

            if (tx != null)
                return tx;

            // try get from network
            var (txFromNetwork, error) = await blockchainApi
                .GetTransactionAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return (T)txFromNetwork;
        }
    }
}