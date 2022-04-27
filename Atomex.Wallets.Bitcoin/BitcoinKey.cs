using System;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;

using Atomex.Common.Memory;

namespace Atomex.Wallets.Bitcoin
{
    /// <summary>
    /// Represents asymmetric key for Bitcoin based coins
    /// </summary>
    /// <inheritdoc/>
    public class BitcoinKey : IKey
    {
        public const int HashSize = 32;

        protected readonly SecureBytes _privateKey;
        private bool disposed;

        public SignDataType SignDataType => SignDataType.Hash;

        public BitcoinKey(SecureBytes seed)
        {
            _privateKey = seed.Copy();
        }

        public SecureBytes GetPrivateKey() => _privateKey.Copy();
 
        public SecureBytes GetPublicKey()
        {
            using var key = GetKey();

            var privateKey = key.ToBytes();

            return new SecureBytes(key.PubKey.ToBytes());
        }

        public virtual byte[] Sign(
            ReadOnlySpan<byte> data)
        {
            return SignHash(data, SigHash.All);
        }

        public byte[] SignHash(
            ReadOnlySpan<byte> hash,
            SigHash sigHash,
            bool useLowR = true)
        {
            if (hash.Length != HashSize)
                throw new ArgumentException($"Hash size must be {HashSize} bytes ({HashSize * 8} bits)", nameof(hash));

            using var key = GetKey();

            return key
                .Sign(new uint256(hash.ToArray()), new SigningOptions(sigHash, useLowR))
                .ToBytes();
        }

        public virtual Task<byte[]> SignAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Sign(data.Span), cancellationToken);
        }

        public virtual bool Verify(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signature)
        {
            if (data.Length != HashSize)
                throw new ArgumentException($"Data size must be {HashSize} bytes ({HashSize * 8} bits)", nameof(data));

            using var key = GetKey();

            return key.PubKey.Verify(
                hash: new uint256(data.ToArray()),
                sig: new TransactionSignature(signature.ToArray()).Signature);
        }

        public virtual Task<bool> VerifyAsync(
            ReadOnlyMemory<byte> data,
            ReadOnlyMemory<byte> signature,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Verify(data.Span, signature.Span), cancellationToken);
        }

        protected virtual Key GetKey()
        {
            using var unmanagedBytes = _privateKey.ToUnmanagedBytes();

            // todo: use other secured framework for secp256r1 keys instead NBitcoin
            return new Key(unmanagedBytes.ToBytes());
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                    _privateKey.Dispose();

                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}