using System;
using System.Linq;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Cryptography;
using Atomex.Cryptography.BouncyCastle;
using NBitcoin;
using NBitcoin.BouncyCastle.Math;
using NBitcoin.Crypto;

namespace Atomex.Wallet.Tezos
{
    public class TezosExtKey : IExtKey
    {
        private const int ExtendedPrivateKeyLength = 64;
        private const int PrivateKeyLength = 32;
        private const int ChainCodeLength = 32;
        private const int ScalarBytes = 32;

        private Keys Keys { get; }
        private byte[] ChainCode { get; }
        private uint Child { get; }
        private uint Depth { get; }
        private uint Fingerprint { get; }

        protected TezosExtKey()
        {
        }

        public TezosExtKey(byte[] seed)
        {
            //var masterSecret = Hashes.HMACSHA512(key: HashKey, data: seed);
            var masterSecret = new byte[PrivateKeyLength];

            Buffer.BlockCopy(
                src: seed,
                srcOffset: 0,
                dst: masterSecret,
                dstOffset: 0,
                count: 32);         

            // check third highest bit of the last byte of kL, where
            // k = H512(masterSecret) and kL is its left 32-bytes
            byte[] k;

            while (true)
            {
                k = Hashes.SHA512(masterSecret);

                if ((k[31] & 0b00100000) > 0)
                {
                    // discard current k and try to get new
                    Buffer.BlockCopy(
                        src: k,
                        srcOffset: 0,
                        dst: masterSecret,
                        dstOffset: 0,
                        count: 32);

                    k.Clear();
                }
                else break;
            }

            PruneScalar(k);

            Ed25519.GeneratePublicKeyFromExtended(
                extendedPrivateKey: k,
                publicKey: out var publicKey);

            var prefix = new byte[] {0x01};
            var data = prefix.ConcatArrays(masterSecret);
            ChainCode = Hashes.SHA256(data);

            Keys = new Keys(sk: k, pk: publicKey);

            masterSecret.Clear();
            k.Clear();
            data.Clear();
            publicKey.Clear();
        }

        protected TezosExtKey(
            Keys keys,
            byte depth,
            uint child,
            byte[] chainCode,
            uint fingerPrint)
        {
            Keys = keys;
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
            var childKeys = Derive(
                chainCode: ChainCode,
                child: index,
                childChainCode: out var childChainCode);

            GetPublicKey(out var publicKey);

            try
            {
                var fingerPrint = Utils.ToUInt32(
                    value: Hashes.Hash160(publicKey).ToBytes(),
                    littleEndian: true);

                return new TezosExtKey(
                    keys: childKeys,
                    depth: (byte)(Depth + 1),
                    child: index,
                    chainCode: childChainCode,
                    fingerPrint: fingerPrint);
            }
            finally
            {
                publicKey.Clear();
            }
        }

        public IExtKey Derive(KeyPath keyPath)
        {
            if (keyPath == null)
                throw new ArgumentNullException(nameof(keyPath));

            IExtKey result = this;

            return keyPath.Indexes.Aggregate(result, (current, index) => current.Derive(index));
        }

        public void GetPrivateKey(out byte[] privateKey)
        {
            // todo: dot not store key in heap
            privateKey = Base58Check.Decode(Keys.DecryptPrivateKey(), Prefix.Edsk);
        }

        public void GetPublicKey(out byte[] publicKey)
        {
            // todo: dot not store key in heap
            publicKey = Base58Check.Decode(Keys.DecryptPublicKey(), Prefix.Edpk);
        }

        public byte[] SignHash(byte[] hash)
        {
            GetPrivateKey(out var extendedPrivateKey);

            try
            {
                return TezosSigner.SignByExtendedKey(
                    data: hash,
                    extendedPrivateKey: extendedPrivateKey);
            }
            finally
            {
                extendedPrivateKey.Clear();
            }
        }

        public byte[] SignMessage(byte[] data)
        {
            GetPrivateKey(out var extendedPrivateKey);

            try
            {
                return TezosSigner.SignByExtendedKey(
                    data: data,
                    extendedPrivateKey: extendedPrivateKey);
            }
            finally
            {
                extendedPrivateKey.Clear();
            }
        }

        public bool VerifyHash(byte[] hash, byte[] signature)
        {
            GetPublicKey(out var publicKey);

            try
            {
                return TezosSigner.Verify(
                    data: hash,
                    signature: signature,
                    publicKey: publicKey);
            }
            finally
            {
                publicKey.Clear();
            }
        }

        public bool VerifyMessage(byte[] data, byte[] signature)
        {
            GetPublicKey(out var publicKey);

            try
            {
                return TezosSigner.Verify(
                    data: data,
                    signature: signature,
                    publicKey: publicKey);
            }
            finally
            {
                publicKey.Clear();
            }
        }

        private Keys Derive(
            byte[] chainCode,
            uint child,
            out byte[] childChainCode)
        {
            GetPrivateKey(out var k); // extended 64-bit private key k
            GetPublicKey(out var publicKey);

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
            byte[] childPublicKey = null;

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
                    count: 32);

                Ed25519.GeneratePublicKeyFromExtended(
                    extendedPrivateKey: childK,
                    publicKey: out childPublicKey);

                return new Keys(childK, childPublicKey);
            }
            finally
            {
                publicKey.Clear();
                k.Clear();
                keyData.Clear();
                z.Clear();
                c.Clear();
                childK.Clear();
                childPublicKey.Clear();
                klNumberBytes.Clear();
                klBytes.Clear();
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
    }
}