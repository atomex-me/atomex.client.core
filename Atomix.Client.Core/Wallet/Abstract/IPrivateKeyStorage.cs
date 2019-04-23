using System.Threading;
using System.Threading.Tasks;
using Atomix.Core.Entities;

namespace Atomix.Wallet.Abstract
{
    public interface IPrivateKeyStorage
    {
        byte[] GetPrivateKey(Currency currency, KeyIndex keyIndex);

        byte[] GetPublicKey(Currency currency, KeyIndex keyIndex);

        Task<KeyIndex> RecoverKeyIndexAsync(
            WalletAddress walletAddress,
            uint maxIndex,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<KeyIndex> RecoverKeyIndexAsync(
            Currency currency,
            string address,
            uint maxIndex,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}