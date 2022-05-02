using System.Security;

using Atomex.Common.Memory;

namespace Atomex.Wallets.Abstract
{
    /// <summary>
    /// Represents basic key storage interface
    /// </summary>
    public interface IKeyStorage
    {
        /// <summary>
        /// Gets private key by key index
        /// </summary>
        /// <param name="keyIndex">Key index</param>
        /// <returns>Private key secure bytes</returns>
        SecureBytes GetPrivateKey(int keyIndex);

        /// <summary>
        /// Gets mnemonic phrase by key index
        /// </summary>
        /// <param name="keyIndex">Key index</param>
        /// <returns>Mnemonic phrase if exists, otherwise null (for old wallets)</returns>
        SecureString GetMnemonic(int keyIndex);

        /// <summary>
        /// Keys count
        /// </summary>
        int KeysCount { get; }
    }
}