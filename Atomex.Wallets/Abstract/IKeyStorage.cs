using Atomex.Common.Memory;

namespace Atomex.Wallets.Abstract
{
    /// <summary>
    /// Represents basic key storage interface
    /// </summary>
    public interface IKeyStorage
    {
        /// <summary>
        /// Gets private key by index
        /// </summary>
        /// <param name="keyIndex">Key index</param>
        /// <returns>Private key secure bytes</returns>
        SecureBytes GetPrivateKey(int keyIndex);
    }
}