using System.Collections.Generic;
using System.Threading.Tasks;
using Atomix.Core.Entities;

namespace Atomix.Blockchain.Abstract
{
    public interface ITxOutputSource
    {
        Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(IEnumerable<WalletAddress> addresses);
    }
}