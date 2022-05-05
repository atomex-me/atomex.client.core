using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Common;

namespace Atomex.Wallets.Ethereum.Common
{
    public static class GasLimitExtensions
    {
        public static async Task<(int? gasLimit, Error error)> GetGasLimitAsync(
            this GasLimit gasLimit,
            IEthereumApi api,
            string to,
            string from = null,
            BigInteger? value = null,
            BigInteger? gasPrice = null,
            string data = null,
            CancellationToken cancellationToken = default)
        {
            Error error = null;

            if (gasLimit.UseNetwork)
            {
                var (estimatedGas, gasLimitError) = await api
                    .EstimateGasAsync(to, from, value, gasPrice, gasLimit: 0, data, cancellationToken)
                    .ConfigureAwait(false);

                if (gasLimitError == null && estimatedGas != null)
                    return (gasLimit: estimatedGas, error: null);

                error = gasLimitError ?? (estimatedGas == null
                    ? new Error(Errors.GetGasLimitError, "Gas limit is null")
                    : null);
            }

            if (gasLimit.UseValue)
                return (gasLimit: gasLimit.Value, error: null);

            return (gasLimit: null, error);
        }
    }
}