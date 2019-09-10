using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Wallet.Abstract;

namespace Atomex.Blockchain.Abstract
{
    public interface IInOutTransaction : IBlockchainTransaction
    {
        ITxPoint[] Inputs { get; }
        ITxOutput[] Outputs { get; }

        Task<bool> SignAsync(
            IAddressResolver addressResolver,
            IKeyStorage keyStorage,
            IEnumerable<ITxOutput> spentOutputs,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}