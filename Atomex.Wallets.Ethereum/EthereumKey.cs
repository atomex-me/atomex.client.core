using System;
using System.Threading;
using System.Threading.Tasks;

using Nethereum.Signer;

using Atomex.Common.Memory;

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

        public SignDataType SignDataType => SignDataType.Hash;

        public EthereumKey(SecureBytes seed)
        {
            _privateKey = seed.Copy();
        }

        public SecureBytes GetPrivateKey() => _privateKey.Copy();

        public SecureBytes GetPublicKey()
        {
            var key = GetKey();

            return new SecureBytes(key.GetPubKey());
        }

        public virtual byte[] Sign(
            ReadOnlySpan<byte> data)
        {
            var key = GetKey();

            return key
                .Sign(data.ToArray())
                //.SignAndCalculateYParityV(data.ToArray())
                .ToDER();
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
            var key = GetKey();

            return key.Verify(
                hash: data.ToArray(),
                sig: EthECDSASignature.FromDER(signature.ToArray()));
        }

        public virtual Task<bool> VerifyAsync(
            ReadOnlyMemory<byte> data,
            ReadOnlyMemory<byte> signature,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Verify(data.Span, signature.Span), cancellationToken);
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
