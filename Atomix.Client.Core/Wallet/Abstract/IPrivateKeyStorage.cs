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
            CancellationToken cancellationToken = default(CancellationToken));

        Task<KeyIndex> RecoverKeyIndexAsync(
            Currency currency,
            string address,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}