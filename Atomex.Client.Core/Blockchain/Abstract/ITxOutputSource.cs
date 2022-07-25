using System.Collections.Generic;
using System.Threading.Tasks;

using Atomex.Blockchain.BitcoinBased;
using Atomex.Core;

namespace Atomex.Blockchain.Abstract
{
    public interface ITxOutputSource
    {
        Task<IEnumerable<BitcoinBasedTxOutput>> GetAvailableOutputsAsync(
            IEnumerable<WalletAddress> addresses);

        Task<IEnumerable<BitcoinBasedTxOutput>> GetAvailableOutputsAsync(
            IEnumerable<string> addresses);
    }
}