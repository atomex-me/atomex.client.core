using System;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

using Atomex.Common.Memory;
using Atomex.Cryptography;

namespace Atomex.Wallets.BitcoinBased
{
    /// <summary>
    /// Represents asymmetric key for Bitcoin based coins
    /// </summary>
    /// <inheritdoc/>
    public class BitcoinBasedKey : IKey
    {
        protected readonly SecureBytes _privateKey;
        private bool disposed;

        public BitcoinBasedKey(SecureBytes seed)
        {
            _privateKey = seed.Copy();
        }

        public virtual SecureBytes GetPrivateKey() => _privateKey.Copy();
 
        public virtual SecureBytes GetPublicKey()
        {
            using var key = GetKey();

            return new SecureBytes(key.PubKey.ToBytes());
        }

        public virtual byte[] SignHash(ReadOnlySpan<byte> hash)
        {
            return SignHash(hash, SigHash.All);
        }

        public virtual byte[] SignHash(ReadOnlySpan<byte> hash, SigHash sigHash, bool useLowR = true)
        {
            using var key = GetKey();

            return key
                .Sign(new uint256(hash.ToArray()), sigHash, useLowR)
                .ToBytes();
        }

        public virtual Task<byte[]> SignHashAsync(
            ReadOnlyMemory<byte> hash,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => SignHash(hash.Span), cancellationToken);
        }

        public virtual bool VerifyHash(ReadOnlySpan<byte> hash, ReadOnlySpan<byte> signature)
        {
            using var key = GetKey();

            return key.PubKey.Verify(
                hash: new uint256(hash.ToArray()),
                sig: new TransactionSignature(signature.ToArray()).Signature);
        }

        public virtual Task<bool> VerifyHashAsync(
            ReadOnlyMemory<byte> hash,
            ReadOnlyMemory<byte> signature,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => VerifyHash(hash.Span, signature.Span), cancellationToken);
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