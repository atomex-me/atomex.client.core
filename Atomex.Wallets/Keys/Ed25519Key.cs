using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common.Memory;
using Atomex.Cryptography.BouncyCastle;

namespace Atomex.Wallets.Keys
{
    /// <summary>
    /// Represents asymmetric key for Ed25519
    /// </summary>
    /// <inheritdoc/>
    public class Ed25519Key : IKey
    {
        public const int PrivateKeySize = 32;

        protected SecureBytes _privateKey;
        protected SecureBytes _publicKey;
        private bool _disposed;

        public SignDataType SignDataType => SignDataType.Plain;
        public SecureBytes GetPrivateKey() => _privateKey.Copy();
        public SecureBytes GetPublicKey() => _publicKey.Copy();

        protected Ed25519Key() { }

        public Ed25519Key(SecureBytes seed)
        {
            _privateKey = seed.Copy();

            BcEd25519.GeneratePublicKey(_privateKey, out _publicKey);
        }

        public byte[] Sign(
            ReadOnlySpan<byte> data)
        {
            using var unmanagedPrivateKey = _privateKey.ToUnmanagedBytes();

            return _privateKey.Length == PrivateKeySize
                ? BcEd25519.Sign(unmanagedPrivateKey, data)
                : BcEd25519.SignWithExtendedKey(unmanagedPrivateKey, data);
        }

        public Task<byte[]> SignAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Sign(data.Span), cancellationToken);
        }

        public bool Verify(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signature)
        {
            using var unmanagedPublicKey = _publicKey.ToUnmanagedBytes();

            return BcEd25519.Verify(unmanagedPublicKey, data, signature);
        }

        public Task<bool> VerifyAsync(
            ReadOnlyMemory<byte> data,
            ReadOnlyMemory<byte> signature,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Verify(data.Span, signature.Span), cancellationToken);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _privateKey.Dispose();
                    _publicKey.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}