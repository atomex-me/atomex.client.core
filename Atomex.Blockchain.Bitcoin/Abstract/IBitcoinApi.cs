using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Bitcoin.Abstract
{
    public interface IBitcoinApi : IBlockchainApi
    {
        Task<(IEnumerable<BitcoinTxOutput> outputs, Error error)> GetOutputsAsync(
            string address,
            CancellationToken cancellationToken = default);
    }
}