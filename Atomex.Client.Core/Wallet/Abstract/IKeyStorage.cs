using Atomex.Common;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IKeyStorage
    {
        SecureBytes GetPrivateKey(CurrencyConfig currency, KeyIndex keyIndex);
        SecureBytes GetPublicKey(CurrencyConfig currency, KeyIndex keyIndex);
    }
}