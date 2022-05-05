using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Common;

namespace Atomex.Wallets.Ethereum.Common
{
    public static class GasPriceExtensions
    {
        public static async Task<(decimal? gasPrice, Error error)> ResolveGasPriceAsync(
            this GasPrice gasPrice,
            IEthereumApi api,
            CancellationToken cancellationToken = default)
        {
            Error error = null;

            if (gasPrice.UseNetwork)
            {
                var (fastGasPrice, gasPriceError) = await api
                    .GetFastGasPriceAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (gasPriceError == null && fastGasPrice != null)
                    return (gasPrice: fastGasPrice, error: null);

                error = gasPriceError ?? (fastGasPrice == null
                    ? new Error(Errors.GetFastGasPriceError, "Gas price is null")
                    : null);
            }

            if (gasPrice.UseValue)
                return (gasPrice: gasPrice.Value, error: null);

            return (gasPrice: null, error);
        }
    }
}