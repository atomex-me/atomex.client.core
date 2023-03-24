using Atomex.Common.Memory;

namespace Atomex.Wallets.Abstract
{
    /// <summary>
    /// Represents basic key storage interface
    /// </summary>
    public interface IKeyStorage
    {
        SecureBytes GetPrivateKey(
            CurrencyConfig currency,
            string keyPath,
            int keyType);

        byte[] GetPublicKey(
            CurrencyConfig currency,
            string keyPath,
            int keyType);
    }
}