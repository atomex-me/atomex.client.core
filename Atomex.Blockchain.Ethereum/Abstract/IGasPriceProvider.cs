using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Ethereum.EtherScan;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum.Abstract
{
    public interface IGasPriceProvider
    {
        Task<Result<GasPriceDto>> GetOracleGasPriceAsync(
            CancellationToken cancellationToken = default);
    }
}