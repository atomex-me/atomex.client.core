using System;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;
using NBitcoin.Crypto;
using Utils = NBitcoin.Utils;
using Org.BouncyCastle.Math;

using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Cryptography.Abstract;
using Atomex.Cryptography.BouncyCastle;

namespace Atomex.Wallets.Keys
{
    /// <summary>
    /// Represents asymmetric Hierarchical Deterministic key for Ed25519
    /// definded in https://ieeexplore.ieee.org/document/7966967
    /// </summary>
    /// <inheritdoc/>
    public class Bip32Ed25519ExtKey : Ed25519Key, IExtKey
    {
        private const int ExtendedPrivateKeyLength = 64;
        private const int PrivateKeyLength = 32;
        private const int ChainCodeLength = 32;
        private const int ScalarBytes = 32;

        private readonly SecureBytes _chainCode;
        private readonly uint _child;
        private readonly uint _depth;
        private readonly uint _fingerPrint;
        private bool _disposed;

        protected Bip32Ed25519ExtKey(
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

        public Bip32Ed25519ExtKey(SecureBytes seed)
        {
            using var unmanagedSeed = seed.ToUnmanagedBytes();

            using var masterSecret = new UnmanagedBytes(unmanagedSeed
                .GetReadOnlySpan()[..PrivateKeyLength]);

            // check third highest bit of the last byte of kL, where
            // k = H512(masterSecret) and kL is its left 32-bytes
            while (true)
            {
                using var k = new UnmanagedBytes(64);

                HashAlgorithm.Sha512.Hash(masterSecret, k);

                if ((k[31] & 0b00100000) > 0)
                {
                    // discard current k and try to get new
                    k.GetReadOnlySpan()[..PrivateKeyLength].CopyTo(masterSecret);
                }
                else
                {
                    PruneScalar(k);

                    _privateKey = new SecureBytes(k);
                    break;
                }
            }

            BcEd25519.GeneratePublicKeyFromExtended(
                extendedPrivateKey: _privateKey,
                publicKey: out _publicKey);

            using var data = new UnmanagedBytes(33);

            data[0] = 0x01;
            masterSecret.CopyTo(data.GetSpan().Slice(1, PrivateKeyLength));

            using var unmanagedChainCode = new UnmanagedBytes(32);

            HashAlgorithm.Sha256.Hash(data, unmanagedChainCode);

            _chainCode = new SecureBytes(unmanagedChainCode);
        }

        public IExtKey Derive(uint index)
        {
            using var privateKey = _privateKey.ToUnmanagedBytes();
            using var publicKey = _publicKey.ToUnmanagedBytes();

            var keyData = index >> 31 == 0
                ? new UnmanagedBytes(1 + 32 + 4)
                : new UnmanagedBytes(1 + 64 + 4);

            using var chainData = index >> 31 == 0
                ? new UnmanagedBytes(1 + 32 + 4)
                : new UnmanagedBytes(1 + 64 + 4);

            if (index >> 31 == 0)
            {
                keyData[0] = 0x02;
                publicKey.CopyTo(keyData.GetSpan().Slice(1, 32));

                chainData[0] = 0x03;
                publicKey.CopyTo(chainData.GetSpan().Slice(1, 32));
            }
            else // hardened key (private derivation)
            {
                keyData[0] = 0x00;
                privateKey.CopyTo(keyData.GetSpan().Slice(1, 64));

                chainData[0] = 0x01;
                privateKey.CopyTo(chainData.GetSpan().Slice(1, 64));
            }

            var indexBytes = IndexToBytes(index);
            var indexBytesSpan = new ReadOnlySpan<byte>(indexBytes);

            indexBytesSpan.CopyTo(keyData.GetSpan().Slice(keyData.Length - 4, 4));
            indexBytesSpan.CopyTo(chainData.GetSpan().Slice(chainData.Length - 4, 4));

            using var unmanagedChainCode = _chainCode.ToUnmanagedBytes();

            var k = privateKey.ToBytes();
            BigInteger kl, kr;

            while (true)
            {
                var z = MacAlgorithm.HmacSha512.Mac(unmanagedChainCode, keyData);

                // todo: use custom big integer with span support
                var zl = new BigInteger(sign: 1, bytes: z, offset: 0, length: 28);
                var zr = new BigInteger(sign: 1, bytes: z, offset: 32, length: 32);

                var klp = new BigInteger(sign: 1, bytes: k, offset: 0, length: 32);
                var krp = new BigInteger(sign: 1, bytes: k, offset: 32, length: 32);

                var eight = BigInteger.ValueOf(8);
                var module = BigInteger.One.ShiftLeft(256);

                kl = zl.Multiply(eight).Add(klp); // 8 * zl + klp
                kr = zr.Add(krp).Mod(module); // (zr + krp) mod 2^256

                // 2^252 + 27742317777372353535851937790883648493
                var n = BigInteger.One
                    .ShiftLeft(252)
                    .Add(new BigInteger(str: "14DEF9DEA2F79CD65812631A5CF5D3ED", radix: 16));

                if (kl.CompareTo(n) >= 0 && kl.Remainder(n).Equals(BigInteger.Zero))
                {
                    // Kl is divisible by the base order n, discard kl
                    keyData.Dispose();
                    keyData = new UnmanagedBytes(z);

                    z.Clear();
                }
                else break;
            }

            using var c = new UnmanagedBytes(64);
            MacAlgorithm.HmacSha512.Mac(unmanagedChainCode, chainData, c);

            using var childChainCode = new UnmanagedBytes(c.GetSpan().Slice(32, ChainCodeLength));

            var klBytes = new byte[32];
            kl.ToByteArrayUnsigned().AsSpan().CopyTo(klBytes);

            var krBytes = new byte[32];
            kr.ToByteArrayUnsigned().AsSpan().CopyTo(krBytes);

            PruneScalar(klBytes);

            using var unmanagedChildPrivateKey = new UnmanagedBytes(ExtendedPrivateKeyLength);

            klBytes.CopyTo(unmanagedChildPrivateKey.GetSpan()[..PrivateKeyLength]);

            krBytes.CopyTo(unmanagedChildPrivateKey.GetSpan().Slice(PrivateKeyLength, PrivateKeyLength));

            var childPrivateKey = new SecureBytes(unmanagedChildPrivateKey);

            BcEd25519.GeneratePublicKeyFromExtended(
                childPrivateKey,
                out var childPublicKey);

            // todo: use self hash160 and utils to uint32
            var fingerPrint = Utils.ToUInt32(
                value: Hashes.Hash160(childPublicKey.ToUnsecuredBytes()).ToBytes(),
                littleEndian: true);

            return new Bip32Ed25519ExtKey(
                privateKey: childPrivateKey,
                publicKey: childPublicKey,
                chainCode: new SecureBytes(childChainCode),
                child: index,
                depth: (byte)(_depth + 1),
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
            num[0] = (byte)(index >> 24 & 0xFF);
            num[1] = (byte)(index >> 16 & 0xFF);
            num[2] = (byte)(index >> 8 & 0xFF);
            num[3] = (byte)(index >> 0 & 0xFF);

            return num;
        }

        private static void PruneScalar(Span<byte> data)
        {
            data[0] &= 0b11111000;  // the lowest 3 bits kL are cleared (&= 0xF8)
            data[ScalarBytes - 1] &= 0b01111111; // the highest bit kL is cleared (&= 7F)
            data[ScalarBytes - 1] |= 0b01000000; // the second highest bit kL is set (|= 0x40)
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