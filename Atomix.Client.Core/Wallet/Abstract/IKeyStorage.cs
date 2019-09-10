using Atomix.Core.Entities;

namespace Atomix.Wallet.Abstract
{
    public interface IKeyStorage
    {
        byte[] GetPrivateKey(Currency currency, KeyIndex keyIndex);
        byte[] GetPublicKey(Currency currency, KeyIndex keyIndex);
    }
}