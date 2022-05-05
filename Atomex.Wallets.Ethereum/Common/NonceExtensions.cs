using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Common;

namespace Atomex.Wallets.Ethereum.Common
{
    public static class NonceExtensions
    {
        public static async Task<(long? nonce, Error error)> GetNonceAsync(
            this Nonce nonce,
            IEthereumApi api,
            string from,
            CancellationToken cancellationToken = default)
        {
            Error error = null;

            if (nonce.UseNetwork)
            {
                var (nonceFromNetwork, nonceError) = await api
                    .GetTransactionsCountAsync(from, nonce.UsePending, cancellationToken)
                    .ConfigureAwait(false);

                if (nonceFromNetwork != null && nonceError == null)
                {
                    if (nonce.UseOffline)
                    {
                        var offlineNonce = EthereumOfflineNonce.Instance.GetOfflineNonce(
                            address: from,
                            nonceFromNetwork: nonceFromNetwork.Value,
                            isPendingNonce: nonce.UsePending);

                        return (nonce: offlineNonce, error: null);
                    }

                    return (nonce: nonceFromNetwork, error: null);
                }

                error = nonceError ?? (nonceFromNetwork == null
                    ? new Error(Errors.GetTransactionsCountError, "Transactions count is null")
                    : null);
            }

            if (nonce.UseValue)
                return (nonce: nonce.Value, error: null);

            return (nonce: null, error);
        }
    }
}