using System.Collections.Generic;
using System.Threading.Tasks;
using Atomix.Core.Entities;

namespace Atomix.Blockchain.Abstract
{
    public interface ITxOutputSource
    {
        Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            IEnumerable<WalletAddress> addresses);

        Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(
            Currency currency,
            IEnumerable<string> addresses);
    }
}