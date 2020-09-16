using System;
using System.Threading;
using System.Threading.Tasks;
using Nethereum.Signer;

using Atomex.Common.Memory;
using Atomex.Cryptography;

namespace Atomex.Wallets.Ethereum
{
    /// <summary>
    /// Represents asymmetric key for Ethereum
    /// </summary>
    /// <inheritdoc/>
    public class EthereumKey : IKey
    {
        protected readonly SecureBytes _privateKey;
        private bool disposed;

        public EthereumKey(SecureBytes seed)
        {
            _privateKey = seed.Copy();
        }

        public virtual SecureBytes GetPrivateKey() => _privateKey.Copy();

        public SecureBytes GetPublicKey()
        {
            var key = GetKey();

            return new SecureBytes(key.GetPubKey());
        }

        public virtual byte[] SignHash(ReadOnlySpan<byte> hash)
        {
            var key = GetKey();

            return key
                .Sign(hash.ToArray())
                .ToDER();
        }

        public virtual Task<byte[]> SignHashAsync(
            ReadOnlyMemory<byte> hash,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => SignHash(hash.Span), cancellationToken);
        }

        public virtual bool VerifyHash(ReadOnlySpan<byte> hash, ReadOnlySpan<byte> signature)
        {
            var key = GetKey();

            return key.Verify(
                hash: hash.ToArray(),
                sig: EthECDSASignature.FromDER(signature.ToArray()));
        }

        public virtual Task<bool> VerifyHashAsync(
            ReadOnlyMemory<byte> hash,
            ReadOnlyMemory<byte> signature,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => VerifyHash(hash.Span, signature.Span), cancellationToken);
        }

        protected virtual EthECKey GetKey()
        {
            using var unmanagedPrivateKey = _privateKey.ToUnmanagedBytes();

            // todo: use other secured framework for secp256r1 keys instead NEthereum
            return new EthECKey(unmanagedPrivateKey.ToBytes(), true);
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
