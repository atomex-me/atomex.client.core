using System.Collections.Generic;
using System.Threading.Tasks;

using Atomex.Blockchain.Bitcoin;
using Atomex.Core;

namespace Atomex.Blockchain.Abstract
{
    public interface ITxOutputSource
    {
        Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            IEnumerable<WalletAddress> addresses);

        Task<IEnumerable<BitcoinTxOutput>> GetAvailableOutputsAsync(
            IEnumerable<string> addresses);
    }
}