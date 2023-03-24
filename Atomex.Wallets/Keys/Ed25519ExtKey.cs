using System;
using System.Text;

using NBitcoin;
using NBitcoin.Crypto;
using Utils = NBitcoin.Utils;

using Atomex.Common.Memory;
using Atomex.Cryptography.Abstract;
using Atomex.Cryptography.BouncyCastle;

namespace Atomex.Wallets.Keys
{
    /// <summary>
    /// Represents asymmetric Hierarchical Deterministic key for Ed25519
    /// </summary>
    /// <inheritdoc/>
    public class Ed25519ExtKey : Ed25519Key, IExtKey
    {
        private static readonly byte[] HashKey = Encoding.ASCII.GetBytes("ed25519 seed");

        private const int PrivateKeyLength = 32;
        private const int ChainCodeLength = 32;

        private readonly SecureBytes _chainCode;
        private readonly uint _child;
        private readonly uint _depth;
        private readonly uint _fingerPrint;
        private bool _disposed;

        protected Ed25519ExtKey(
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

        public Ed25519ExtKey(SecureBytes seed)
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

            var childPrivateKey = new SecureBytes(hash.GetReadOnlySpan()[..PrivateKeyLength]);
            var childChainCode = new SecureBytes(hash.GetReadOnlySpan().Slice(PrivateKeyLength, ChainCodeLength));

            BcEd25519.GeneratePublicKey(childPrivateKey, out var childPublicKey);

            // todo: use self hash160 and utils to uint32
            var fingerPrint = Utils.ToUInt32(
                value: Hashes.Hash160(childPublicKey.ToUnsecuredBytes()).ToBytes(),
                littleEndian: true);

            return new Ed25519ExtKey(
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

        private static byte[] IndexToBytes(uint index)
        {
            var num = new byte[4];
            num[0] = (byte)(index >> 24 & 0xFF);
            num[1] = (byte)(index >> 16 & 0xFF);
            num[2] = (byte)(index >> 8 & 0xFF);
            num[3] = (byte)(index >> 0 & 0xFF);

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