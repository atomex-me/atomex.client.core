using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Wallets.Abstract
{
    /// <summary>
    /// Represents basic interface for any wallet (also for hardwallets)
    /// </summary>
    public interface IWallet : IDisposable
    {
        /// <summary>
        /// Sign data type
        /// </summary>
        SignDataType SignDataType { get; }

        /// <summary>
        /// Gets public key with key derivation path <paramref name="keyPath"/>
        /// </summary>
        /// <param name="keyPath">Key derivation path</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing an asynchronous public key getting operation and returning public key</returns>
        Task<byte[]> GetPublicKeyAsync(
            string keyPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Signs <paramref name="data"/> with key derivation path <paramref name="keyPath"/>
        /// </summary>
        /// <param name="data">Data to be signed</param>
        /// <param name="keyPath">Key derivation path</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing an asynchronous sign operation and returning signature</returns>
        Task<byte[]> SignAsync(
            ReadOnlyMemory<byte> data,
            string keyPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Signs a list of data with keys from list of key derivation pathes <paramref name="keyPathes"/> by one call
        /// </summary>
        /// <param name="data">List of data to be signed</param>
        /// <param name="keyPathes">List of key derivation pathes</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing an asynchronous sign operation and returning list of signatures</returns>
        Task<IList<byte[]>> SignAsync(
            IList<ReadOnlyMemory<byte>> data,
            IList<string> keyPathes,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Verifies <paramref name="signature"/> for <paramref name="data"/> using key with key derivation path <paramref name="keyPath"/>
        /// </summary>
        /// <param name="data">Data to be verified</param>
        /// <param name="signature">Signatue to be verified</param>
        /// <param name="keyPath">Key derivation path</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing an asynchronous verify operation and returning true if successfull otherwise false</returns>    
        Task<bool> VerifyAsync(
            ReadOnlyMemory<byte> data,
            ReadOnlyMemory<byte> signature,
            string keyPath,
            CancellationToken cancellationToken = default);
    }
}