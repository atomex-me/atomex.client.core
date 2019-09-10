using System.Threading;
using System.Threading.Tasks;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;

namespace Atomix.Blockchain.Abstract
{
    public interface IAddressBasedTransaction : IBlockchainTransaction
    {
        Task<bool> SignAsync(
            IKeyStorage keyStorage,
            WalletAddress address,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}