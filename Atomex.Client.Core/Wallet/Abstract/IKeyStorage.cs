using Atomex.Common.Memory;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IKeyStorage
    {
        SecureBytes GetPrivateKey(Currency currency, KeyIndex keyIndex);
        SecureBytes GetPublicKey(Currency currency, KeyIndex keyIndex);
    }
}