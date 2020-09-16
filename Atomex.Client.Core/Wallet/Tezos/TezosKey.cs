using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Cryptography.BouncyCastle;

namespace Atomex.Wallets.Tezos
{
    /// <summary>
    /// Represents asymmetric key for Tezos
    /// </summary>
    /// <inheritdoc/>
    public class TezosKey : IKey
    {
        public const int PrivateKeySize = 32;

        protected SecureBytes _privateKey;
        protected SecureBytes _publicKey;
        private bool _disposed;

        public virtual SecureBytes GetPrivateKey() => _privateKey.Copy();
        public SecureBytes GetPublicKey() => _publicKey.Copy();

        protected TezosKey() { }

        public TezosKey(SecureBytes seed)
        {
            _privateKey = seed.Copy();

            BcEd25519.GeneratePublicKey(_privateKey, out _publicKey);
        }

        public byte[] SignHash(ReadOnlySpan<byte> hash)
        {
            using var unmanagedPrivateKey = _privateKey.ToUnmanagedBytes();

            return _privateKey.Length == PrivateKeySize
                ? BcEd25519.Sign(unmanagedPrivateKey, hash)
                : BcEd25519.SignWithExtendedKey(unmanagedPrivateKey, hash);
        }

        public Task<byte[]> SignHashAsync(
            ReadOnlyMemory<byte> hash,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => SignHash(hash.Span), cancellationToken);
        }

        public bool VerifyHash(ReadOnlySpan<byte> hash, ReadOnlySpan<byte> signature)
        {
            using var unmanagedPublicKey = _publicKey.ToUnmanagedBytes();

            return BcEd25519.Verify(unmanagedPublicKey, hash, signature);
        }

        public Task<bool> VerifyHashAsync(
            ReadOnlyMemory<byte> hash,
            ReadOnlyMemory<byte> signature,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => VerifyHash(hash.Span, signature.Span), cancellationToken);
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