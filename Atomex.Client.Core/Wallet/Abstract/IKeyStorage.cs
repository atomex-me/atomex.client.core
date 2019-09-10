using Atomex.Core.Entities;

namespace Atomex.Wallet.Abstract
{
    public interface IKeyStorage
    {
        byte[] GetPrivateKey(Currency currency, KeyIndex keyIndex);
        byte[] GetPublicKey(Currency currency, KeyIndex keyIndex);
    }
}