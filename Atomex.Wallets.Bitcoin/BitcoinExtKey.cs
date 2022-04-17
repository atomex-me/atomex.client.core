using System;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;

using Atomex.Common;
using Atomex.Common.Memory;

namespace Atomex.Wallets.Bitcoin
{
    /// <summary>
    /// Represents asymmetric Hierarchical Deterministic key for Bitcoin based 
    /// </summary>
    /// <inheritdoc/>
    public class BitcoinExtKey : BitcoinKey, IExtKey
    {
        public BitcoinExtKey(SecureBytes seed)
            : base(seed)
        {
        }

        protected SecureBytes Derive(Func<ExtKey, ExtKey> derivationFunc)
        {
            var extKey = GetExtKey();
            var childExtKey = derivationFunc(extKey);
            var childExtKeyBytes = childExtKey.PrivateKey.ToBytes();

            try
            {
                return new SecureBytes(childExtKeyBytes);
            }
            finally
            {
                extKey.PrivateKey.Dispose();
                childExtKey.PrivateKey.Dispose();
                childExtKeyBytes.Clear();
            }
        }

        public IExtKey Derive(uint index)
        {
            using var privateKey = Derive(extKey => extKey.Derive(index));
            
            return new BitcoinExtKey(privateKey);
        }

        public IExtKey Derive(string keyPath)
        {
            using var privateKey = Derive(extKey => extKey.Derive(new KeyPath(keyPath)));

            return new BitcoinExtKey(privateKey);
        }

        public Task<IExtKey> DeriveAsync(
            uint index,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Derive(index), cancellationToken);
        }

        public Task<IExtKey> DeriveAsync(
            string keyPath,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Derive(keyPath), cancellationToken);
        }

        private ExtKey GetExtKey()
        {
            using var unmanagedBytes = _privateKey.ToUnmanagedBytes();

            // todo: use other secured framework for secp256r1 keys instead NBitcoin
            return new ExtKey(unmanagedBytes.ToBytes().ToHexString());
        }

        protected override Key GetKey() => GetExtKey().PrivateKey;
    }
}