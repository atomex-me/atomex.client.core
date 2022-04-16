using System;
using System.Linq;

using NBitcoin;
using NBitcoin.BouncyCastle.Math;
using NBitcoin.Crypto;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Cryptography.BouncyCastle;
using Utils = NBitcoin.Utils;

namespace Atomex.Wallet.Tezos
{
    public class Bip32TezosExtKey : IExtKey
    {
        private const int ExtendedPrivateKeyLength = 64;
        private const int PrivateKeyLength = 32;
        private const int ChainCodeLength = 32;
        private const int ScalarBytes = 32;

        private readonly SecureBytes _privateKey;
        private readonly SecureBytes _publicKey;
        private bool disposedValue;

        private byte[] ChainCode { get; }
        private uint Child { get; }
        private uint Depth { get; }
        private uint Fingerprint { get; }

        protected Bip32TezosExtKey()
        {
        }

        public Bip32TezosExtKey(SecureBytes seed)
        {
            var scopedSeed = seed.ToUnsecuredBytes();
            //var masterSecret = Hashes.HMACSHA512(key: HashKey, data: seed);
            var masterSecret = new byte[PrivateKeyLength];

            Buffer.BlockCopy(
                src: scopedSeed,
                srcOffset: 0,
                dst: masterSecret,
                dstOffset: 0,
                count: 32);

            // check third highest bit of the last byte of kL, where
            // k = H512(masterSecret) and kL is its left 32-bytes

            while (true)
            {
                var k = Hashes.SHA512(masterSecret);

                if ((k[31] & 0b00100000) > 0)
                {
                    // discard current k and try to get new
                    Buffer.BlockCopy(
                        src: k,
                        srcOffset: 0,
                        dst: masterSecret,
                        dstOffset: 0,
                        count: 32);
                }
                else
                {
                    _privateKey = new SecureBytes(k);
                    break;
                };
            }

            PruneScalar(_privateKey);

            BcEd25519.GeneratePublicKeyFromExtended(
                extendedPrivateKey: _privateKey,
                publicKey: out _publicKey);

            var prefix = new byte[] { 0x01 };
            var data = prefix.ConcatArrays(masterSecret);
            ChainCode = Hashes.SHA256(data);
        }

        protected Bip32TezosExtKey(
            SecureBytes privateKey,
            SecureBytes publicKey,
            byte depth,
            uint child,
            byte[] chainCode,
            uint fingerPrint)
        {
            _privateKey = privateKey.Copy();
            _publicKey = publicKey.Copy();

            Depth = depth;
            Child = child;
            Fingerprint = fingerPrint;
            ChainCode = new byte[ChainCodeLength];

            Buffer.BlockCopy(
                src: chainCode,
                srcOffset: 0,
                dst: ChainCode,
                dstOffset: 0,
                count: ChainCodeLength);
        }

        public IExtKey Derive(uint index)
        {
            var (childPrivateKey, childPublicKey) = Derive(
                chainCode: ChainCode,
                child: index,
                childChainCode: out var childChainCode);

            using var securePublicKey = GetPublicKey();
            var scopedPublicKey = securePublicKey.ToUnsecuredBytes();

            var fingerPrint = Utils.ToUInt32(
                value: Hashes.Hash160(scopedPublicKey).ToBytes(),
                littleEndian: true);

            return new Bip32TezosExtKey(
                privateKey: childPrivateKey,
                publicKey: childPublicKey,
                depth: (byte)(Depth + 1),
                child: index,
                chainCode: childChainCode,
                fingerPrint: fingerPrint);
        }

        public IExtKey Derive(KeyPath keyPath)
        {
            if (keyPath == null)
                throw new ArgumentNullException(nameof(keyPath));

            IExtKey result = this;

            return keyPath.Indexes.Aggregate(result, (current, index) => current.Derive(index));
        }

        public SecureBytes GetPrivateKey()
        {
            return _privateKey.Copy();
        }

        public SecureBytes GetPublicKey()
        {
            return _publicKey.Copy();
        }

        public byte[] SignHash(byte[] hash)
        {
            var scopedExtendedPrivateKey = _privateKey.ToUnsecuredBytes();

            return TezosSigner.SignByExtendedKey(
                data: hash,
                extendedPrivateKey: scopedExtendedPrivateKey);
        }

        public byte[] SignMessage(byte[] data)
        {
            var scopedExtendedPrivateKey = _privateKey.ToUnsecuredBytes();

            return TezosSigner.SignByExtendedKey(
                data: data,
                extendedPrivateKey: scopedExtendedPrivateKey);
        }

        public bool VerifyHash(byte[] hash, byte[] signature)
        {
            var scopedPublicKey = _publicKey.ToUnsecuredBytes();

            return TezosSigner.Verify(
                data: hash,
                signature: signature,
                publicKey: scopedPublicKey);
        }

        public bool VerifyMessage(byte[] data, byte[] signature)
        {
            var scopedPublicKey = _publicKey.ToUnsecuredBytes();

            return TezosSigner.Verify(
                data: data,
                signature: signature,
                publicKey: scopedPublicKey);
        }

        private (SecureBytes, SecureBytes) Derive(
            byte[] chainCode,
            uint child,
            out byte[] childChainCode)
        {
            var k = _privateKey.ToUnsecuredBytes();  // extended 64-bit private key k
            var publicKey = _publicKey.ToUnsecuredBytes();

            byte[] keyData;
            byte[] chainData;

            if (child >> 31 == 0)
            {
                keyData = new byte[1 + 32 + 4];
                keyData[0] = 0x02;
                Buffer.BlockCopy(src: publicKey, srcOffset: 0, dst: keyData, dstOffset: 1, count: 32);

                chainData = new byte[1 + 32 + 4];
                chainData[0] = 0x03;
                Buffer.BlockCopy(src: publicKey, srcOffset: 0, dst: chainData, dstOffset: 1, count: 32);
            }
            else // hardened key (private derivation)
            {
                keyData = new byte[1 + 64 + 4];
                keyData[0] = 0x00;
                Buffer.BlockCopy(src: k, srcOffset: 0, dst: keyData, dstOffset: 1, count: 64);

                chainData = new byte[1 + 64 + 4];
                chainData[0] = 0x01;
                Buffer.BlockCopy(src: k, srcOffset: 0, dst: chainData, dstOffset: 1, count: 64);
            }

            // derive private & public keys
            Buffer.BlockCopy(
                src: IndexToBytes(child),
                srcOffset: 0,
                dst: keyData,
                dstOffset: keyData.Length - 4,
                count: 4);

            byte[] z;
            BigInteger kl, kr;

            while (true)
            {
                z = Hashes.HMACSHA512(key: ChainCode, data: keyData);

                var zl = new BigInteger(sign: 1, bytes: z, offset: 0, length: 28);
                var zr = new BigInteger(sign: 1, bytes: z, offset: 32, length: 32);

                var klp = new BigInteger(sign: 1, bytes: k, offset: 0, length: 32);
                var krp = new BigInteger(sign: 1, bytes: k, offset: 32, length: 32);

                var eight = BigInteger.ValueOf(8);
                var module = BigInteger.One.ShiftLeft(256);

                kl = zl.Multiply(eight).Add(klp); // 8 * zl + klp
                kr = zr.Add(krp).Mod(module); // (zr + krp) mod 2^256

                // 2^252 + 27742317777372353535851937790883648493
                var n = BigInteger.One.ShiftLeft(252)
                    .Add(new BigInteger(str: "14DEF9DEA2F79CD65812631A5CF5D3ED", radix: 16));

                if (kl.CompareTo(n) >= 0 && kl.Remainder(n).Equals(BigInteger.Zero))
                {
                    // Kl is divisible by the base order n, discard kl
                    keyData.Clear();
                    keyData = new byte[64];

                    Buffer.BlockCopy(z, 0, keyData, 0, 64);

                    z.Clear();
                }
                else break;
            }

            // derive chain code
            Buffer.BlockCopy(
                src: IndexToBytes(child),
                srcOffset: 0,
                dst: chainData,
                dstOffset: chainData.Length - 4,
                count: 4);

            var c = Hashes.HMACSHA512(key: ChainCode, data: chainData);

            childChainCode = new byte[ChainCodeLength];

            Buffer.BlockCopy(
                src: c,
                srcOffset: 32,
                dst: childChainCode,
                dstOffset: 0,
                count: ChainCodeLength);

            var childK = new byte[ExtendedPrivateKeyLength];

            var klNumberBytes = kl.ToByteArrayUnsigned();

            var klBytes = new byte[ScalarBytes];
            Buffer.BlockCopy(
                src: klNumberBytes,
                srcOffset: 0,
                dst: klBytes,
                dstOffset: 0,
                count: klNumberBytes.Length);

            PruneScalar(klBytes);

            var krBytes = kr.ToByteArrayUnsigned();

            try
            {
                Buffer.BlockCopy(
                    src: klBytes,
                    srcOffset: 0,
                    dst: childK,
                    dstOffset: 0,
                    count: 32);

                Buffer.BlockCopy(
                    src: krBytes,
                    srcOffset: 0,
                    dst: childK,
                    dstOffset: 32,
                    count: krBytes.Length <= 32 ? krBytes.Length : 32);

                var childPrivateKey = new SecureBytes(childK);

                BcEd25519.GeneratePublicKeyFromExtended(
                    extendedPrivateKey: childPrivateKey,
                    publicKey: out var childPublicKey);

                return (childPrivateKey, childPublicKey);
            }
            finally
            {
                keyData.Clear();
                z.Clear();
                c.Clear();
                klNumberBytes.Clear();
                krBytes.Clear();
            }
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

        private static void PruneScalar(byte[] data)
        {
            data[0] &= 0b11111000;  // the lowest 3 bits kL are cleared (&= 0xF8)
            data[ScalarBytes - 1] &= 0b01111111; // the highest bit kL is cleared (&= 7F)
            data[ScalarBytes - 1] |= 0b01000000; // the second highest bit kL is set (|= 0x40)
        }

        private static void PruneScalar(SecureBytes data)
        {
            var scopedData = data.ToUnsecuredBytes();

            PruneScalar(scopedData);

            data.Reset(scopedData);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _privateKey?.Dispose();
                    _publicKey?.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}