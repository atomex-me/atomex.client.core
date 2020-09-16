using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common.Memory;

namespace Atomex.Wallets.Abstract
{
    /// <summary>
    /// Represents basic interface for any wallet (also for hardwallets)
    /// </summary>
    public interface IWallet : IDisposable
    {
        /// <summary>
        /// Gets public key with key derivation path <paramref name="keyPath"/>
        /// </summary>
        /// <param name="keyPath">Key derivation path</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing an asynchronous public key getting operation and returning public key</returns>
        Task<SecureBytes> GetPublicKeyAsync(
            string keyPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Signs <paramref name="hash"/> with key derivation path <paramref name="keyPath"/>
        /// </summary>
        /// <param name="hash">Hash to be signed</param>
        /// <param name="keyPath">Key derivation path</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing an asynchronous sign operation and returning signature</returns>
        Task<byte[]> SignHashAsync(
            ReadOnlyMemory<byte> hash,
            string keyPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Signs a list of hashes with keys from list of key derivation pathes <paramref name="keyPathes"/> by one call
        /// </summary>
        /// <param name="hashes">List of hashes to be signed</param>
        /// <param name="keyPathes">List of key derivation pathes</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing an asynchronous sign operation and returning list of signatures</returns>
        Task<IList<byte[]>> SignHashAsync(
            IList<ReadOnlyMemory<byte>> hashes,
            IList<string> keyPathes,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Verifies <paramref name="signature"/> for <paramref name="hash"/> using key with key derivation path <paramref name="keyPath"/>
        /// </summary>
        /// <param name="hash">Hash to be verified</param>
        /// <param name="signature">Signatue to be verified</param>
        /// <param name="keyPath">Key derivation path</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing an asynchronous verify operation and returning true if successfull otherwise false</returns>    
        Task<bool> VerifyHashAsync(
            ReadOnlyMemory<byte> hash,
            ReadOnlyMemory<byte> signature,
            string keyPath,
            CancellationToken cancellationToken = default);
    }
}