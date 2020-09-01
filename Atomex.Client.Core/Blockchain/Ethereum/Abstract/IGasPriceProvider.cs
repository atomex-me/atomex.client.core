using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Blockchain.Ethereum.Abstract
{
    public class GasPrice
    {
        public long Low { get; set; }
        public long Average { get; set; }
        public long High { get; set; }
    }

    public interface IGasPriceProvider
    {
        Task<Result<GasPrice>> GetGasPriceAsync(
            bool useCache = true,
            CancellationToken cancellationToken = default);
    }
}