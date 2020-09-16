using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common.Memory;

namespace Atomex.Cryptography
{
    /// <summary>
    /// Represents basic interface for asymmetric key
    /// </summary>
    public interface IKey : IDisposable
    {
        /// <summary>
        /// Gets private key
        /// </summary>
        /// <returns>Private key</returns>
        SecureBytes GetPrivateKey();

        /// <summary>
        /// Gets public key
        /// </summary>
        /// <returns>Public key</returns>
        SecureBytes GetPublicKey();

        /// <summary>
        /// Signs <paramref name="hash"/>
        /// </summary>
        /// <param name="hash">Hash to be signed</param>
        /// <returns>Signature</returns>
        byte[] SignHash(ReadOnlySpan<byte> hash);

        /// <summary>
        /// Signs <paramref name="hash"/>
        /// </summary>
        /// <param name="hash">Hash to be signed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing an asynchronous sign operation and returning signature</returns>
        Task<byte[]> SignHashAsync(
            ReadOnlyMemory<byte> hash,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Verifies <paramref name="signature"/> for <paramref name="hash"/>
        /// </summary>
        /// <param name="hash">Hash to be verified</param>
        /// <param name="signature">Signature to be verified</param>
        /// <returns>True if successfull, otherwise false</returns>
        bool VerifyHash(ReadOnlySpan<byte> hash, ReadOnlySpan<byte> signature);

        /// <summary>
        /// Verifies <paramref name="signature"/> for <paramref name="hash"/>
        /// </summary>
        /// <param name="hash">Hash to be verified</param>
        /// <param name="signature">Signature to be verified</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing an asynchronous verify operation and returning verification result</returns>
        Task<bool> VerifyHashAsync(
            ReadOnlyMemory<byte> hash,
            ReadOnlyMemory<byte> signature,
            CancellationToken cancellationToken = default);
    }
}