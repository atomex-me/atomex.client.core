using System.Threading;
using System.Threading.Tasks;
using Atomix.Wallet.Abstract;

namespace Atomix.Blockchain.Abstract
{
    public interface IAddressBasedTransaction : IBlockchainTransaction
    {
        Task<bool> SignAsync(
            IPrivateKeyStorage keyStorage,
            string address,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}