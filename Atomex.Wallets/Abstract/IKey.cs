using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common.Memory;

namespace Atomex.Wallets
{
    public enum SignDataType
    {
        Plain,
        Hash
    }

    /// <summary>
    /// Represents basic interface for asymmetric key
    /// </summary>
    public interface IKey : IDisposable
    {
        /// <summary>
        /// Sign data type
        /// </summary>
        SignDataType SignDataType { get; }

        /// <summary>
        /// Gets private key
        /// </summary>
        /// <returns>Private key</returns>
        SecureBytes GetPrivateKey();

        /// <summary>
        /// Gets public key
        /// </summary>
        /// <returns>Public key</returns>
        byte[] GetPublicKey();

        /// <summary>
        /// Signs <paramref name="data"/>
        /// </summary>
        /// <param name="data">Data to be signed</param>
        /// <returns>Signature</returns>
        byte[] Sign(
            ReadOnlySpan<byte> data);

        /// <summary>
        /// Signs <paramref name="data"/>
        /// </summary>
        /// <param name="data">Data to be signed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing an asynchronous sign operation and returning signature</returns>
        Task<byte[]> SignAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Verifies <paramref name="signature"/> for <paramref name="data"/>
        /// </summary>
        /// <param name="data">Data to be verified</param>
        /// <param name="signature">Signature to be verified</param>
        /// <returns>True if successfull, otherwise false</returns>
        bool Verify(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signature);

        /// <summary>
        /// Verifies <paramref name="signature"/> for <paramref name="data"/>
        /// </summary>
        /// <param name="data">Data to be verified</param>
        /// <param name="signature">Signature to be verified</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing an asynchronous verify operation and returning verification result</returns>
        Task<bool> VerifyAsync(
            ReadOnlyMemory<byte> data,
            ReadOnlyMemory<byte> signature,
            CancellationToken cancellationToken = default);
    }
}