using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Blockchain.Ethereum.Abstract
{
    public interface IGasPriceProvider
    {
        Task<Result<long>> GetFastGasPriceAsync(
            CancellationToken cancellationToken = default);
    }
}