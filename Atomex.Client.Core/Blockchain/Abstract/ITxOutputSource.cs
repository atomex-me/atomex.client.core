using System.Collections.Generic;
using System.Threading.Tasks;
using Atomex.Core;

namespace Atomex.Blockchain.Abstract
{
    public interface ITxOutputSource
    {
        Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            IEnumerable<WalletAddress> addresses);

        Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            IEnumerable<string> addresses);
    }
}