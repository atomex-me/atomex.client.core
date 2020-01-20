using System.Threading;
using System.Threading.Tasks;
using Atomex.Core;
using Atomex.Wallet.Abstract;

namespace Atomex.Blockchain.Abstract
{
    public interface IAddressBasedTransaction : IBlockchainTransaction
    {
        Task<bool> SignAsync(
            IKeyStorage keyStorage,
            WalletAddress address,
            CancellationToken cancellationToken = default);
    }
}