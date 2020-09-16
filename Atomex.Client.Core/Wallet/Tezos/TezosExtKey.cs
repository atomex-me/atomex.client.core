using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Crypto;

using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Cryptography.Abstract;
using Atomex.Cryptography.BouncyCastle;
using Utils = NBitcoin.Utils;

namespace Atomex.Wallets.Tezos
{
    /// <summary>
    /// Represents asymmetric Hierarchical Deterministic key for Tezos
    /// </summary>
    /// <inheritdoc/>
    public class TezosExtKey : TezosKey, IExtKey
    {
        private static readonly byte[] HashKey = Encoding.ASCII.GetBytes("ed25519 seed");

        private const int PrivateKeyLength = 32;
        private const int ChainCodeLength = 32;

        private readonly SecureBytes _chainCode;
        private readonly uint _child;
        private readonly uint _depth;
        private readonly uint _fingerPrint;
        private bool _disposed;

        protected TezosExtKey(
            SecureBytes privateKey,
            SecureBytes publicKey,
            SecureBytes chainCode,
            uint child,
            uint depth,
            uint fingerPrint)
        {
            _privateKey = privateKey;
            _publicKey = publicKey;
            _chainCode = chainCode;
            _child = child;
            _depth = depth;
            _fingerPrint = fingerPrint;
        }

        public TezosExtKey(SecureBytes seed)
        {
            using var unmanagedSeed = seed.ToUnmanagedBytes();
            using var unmanagedSeedHash = new UnmanagedBytes(MacAlgorithm.HmacSha512.HashSize);

            // calculate seed mac
            MacAlgorithm.HmacSha512.Mac(HashKey, unmanagedSeed, unmanagedSeedHash);

            // create keys
            BcEd25519.GenerateKeyPair(
                seed: unmanagedSeedHash,
                privateKey: out _privateKey,
                publicKey: out _publicKey);

            // save chain code as half part ([32..63] bytes) of seed hash
            var unmanagedChainCode = new UnmanagedBytes(ChainCodeLength);

            unmanagedSeedHash
                .GetReadOnlySpan()
                .Slice(PrivateKeyLength, ChainCodeLength)
                .CopyTo(unmanagedChainCode);

            _chainCode = new SecureBytes(unmanagedChainCode);
        }

        public IExtKey Derive(uint index)
        {
            using var unmanagedPrivateKey = _privateKey.ToUnmanagedBytes();
            using var unmanagedPublicKey = _publicKey.ToUnmanagedBytes();

            using var data = new UnmanagedBytes(1 + 32 + 4);

            data[0] = 0;

            if (index >> 31 == 0)
            {
                unmanagedPublicKey
                    .CopyTo(data.GetSpan().Slice(1, 32));
            }
            else // hardened key (private derivation)
            {
                unmanagedPrivateKey
                    .CopyTo(data.GetSpan().Slice(1, 32));
            }

            new ReadOnlySpan<byte>(IndexToBytes(index))
                .CopyTo(data.GetSpan().Slice(33, 4));

            using var unmanagedChainCode = _chainCode.ToUnmanagedBytes();

            using var hash = new UnmanagedBytes(64);

            MacAlgorithm.HmacSha512.Mac(unmanagedChainCode, data, hash);

            var childPrivateKey = new SecureBytes(hash.GetReadOnlySpan().Slice(0, PrivateKeyLength));
            var childChainCode = new SecureBytes(hash.GetReadOnlySpan().Slice(PrivateKeyLength, ChainCodeLength));

            BcEd25519.GeneratePublicKey(childPrivateKey, out var childPublicKey);

            // todo: use self hash160 and utils to uint32
            var fingerPrint = Utils.ToUInt32(
                value: Hashes.Hash160(childPublicKey.ToUnsecuredBytes()).ToBytes(),
                littleEndian: true);

            return new TezosExtKey(
                privateKey: childPrivateKey,
                publicKey: childPublicKey,
                chainCode: childChainCode,
                child: index,
                depth: _depth + 1,
                fingerPrint: fingerPrint);
        }

        public IExtKey Derive(string keyPath)
        {
            IExtKey key = this;
            IExtKey derivedKey = null;

            foreach (var index in new KeyPath(keyPath).Indexes)
            {
                derivedKey = key.Derive(index);

                if (key != this)
                    key.Dispose();

                key = derivedKey;
            }

            return derivedKey;
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

        private static byte[] IndexToBytes(uint index)
        {
            var num = new byte[4];
            num[0] = (byte)((index >> 24) & 0xFF);
            num[1] = (byte)((index >> 16) & 0xFF);
            num[2] = (byte)((index >> 8) & 0xFF);
            num[3] = (byte)((index >> 0) & 0xFF);

            return num;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    _chainCode.Dispose();

                _disposed = true;
            }

            base.Dispose(disposing);
        }
    }
}