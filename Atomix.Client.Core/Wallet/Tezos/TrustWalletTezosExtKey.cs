using System;
using System.Linq;
using Atomix.Blockchain.Tezos;
using Atomix.Blockchain.Tezos.Internal;
using Atomix.Common;
using Atomix.Cryptography;
using Atomix.Cryptography.BouncyCastle;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;

namespace Atomix.Wallet.Tezos
{
    public class TrustWalletTezosExtKey : IExtKey
    {
        private static readonly byte[] HashKey = Encoders.ASCII.DecodeData(encoded: "ed25519 seed");

        private const int PrivateKeyLength = 32;
        private const int ChainCodeLength = 32;

        private Keys Keys { get; }
        private byte[] ChainCode { get; }
        private uint Child { get; }
        private uint Depth { get; }
        private uint Fingerprint { get; }

        public TrustWalletTezosExtKey(byte[] seed)
        {
            var hashSeed = Hashes.HMACSHA512(key: HashKey, data: seed);

            Keys = FromHash(hashSeed);

            ChainCode = new byte[ChainCodeLength];

            // copy hashSeed last 32 bytes to ChainCode
            Buffer.BlockCopy(
                src: hashSeed,
                srcOffset: PrivateKeyLength,
                dst: ChainCode,
                dstOffset: 0,
                count: ChainCodeLength);

            hashSeed.Clear();
        }

        private TrustWalletTezosExtKey(
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

                return new TrustWalletTezosExtKey(
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
            GetPrivateKey(out var privateKey);

            try
            {
                return TezosSigner.Sign(
                    data: hash,
                    privateKey: privateKey);
            }
            finally
            {
                privateKey.Clear();
            }
        }

        public byte[] SignMessage(byte[] data)
        {
            GetPrivateKey(out var privateKey);

            try
            {
                return TezosSigner.Sign(
                    data: data,
                    privateKey: privateKey);
            }
            finally
            {
                privateKey.Clear();
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

        private static Keys FromHash(byte[] hash)
        {
            Keys result;
            byte[] publicKey = null, privateKey = null;

            try
            {
                Ed25519.GenerateKeyPair(
                    seed: hash,
                    privateKey: out privateKey,
                    publicKey: out publicKey);

                result = new Keys(sk: privateKey, pk: publicKey);
            }
            finally
            {
                privateKey.Clear();
                publicKey.Clear();
            }

            return result;
        }

        private Keys Derive(
            byte[] chainCode,
            uint child,
            out byte[] childChainCode)
        {
            GetPublicKey(out var publicKey);
            GetPrivateKey(out var privateKey);

            var data = new byte[1 + 32 + 4];

            if (child >> 31 == 0)
            {
                data[0] = 0;
                Buffer.BlockCopy(src: publicKey, srcOffset: 0, dst: data, dstOffset: 1, count: 32);
            }
            else // hardened key (private derivation)
            {
                data[0] = 0;
                Buffer.BlockCopy(src: privateKey, srcOffset: 0, dst: data, dstOffset: 1, count: 32);
            }

            Buffer.BlockCopy(src: IndexToBytes(child), srcOffset: 0, dst: data, dstOffset: 33, count: 4);

            var l = Hashes.HMACSHA512(key: chainCode, data: data);
            var ll = l.SubArray(start: 0, length: 32);

            childChainCode = new byte[ChainCodeLength];
            var childPrivateKey = new byte[PrivateKeyLength];
            byte[] childPublicKey = null;

            try
            {
                Buffer.BlockCopy(
                    src: l,
                    srcOffset: 32,
                    dst: childChainCode,
                    dstOffset: 0,
                    count: ChainCodeLength);

                Ed25519.GeneratePublicKey(
                    privateKey: ll,
                    publicKey: out childPublicKey);

                Buffer.BlockCopy(
                    src: l,
                    srcOffset: 0,
                    dst: childPrivateKey,
                    dstOffset: 0,
                    count: 32);

                return new Keys(sk: childPrivateKey, pk: childPublicKey);
            }
            finally
            {
                publicKey.Clear();
                privateKey.Clear();
                data.Clear();
                l.Clear();
                ll.Clear();
                childPrivateKey.Clear();
                childPublicKey.Clear();
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
    }
}