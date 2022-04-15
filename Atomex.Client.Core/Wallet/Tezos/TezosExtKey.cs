using System;
using System.Linq;

using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Cryptography.BouncyCastle;
using Utils = NBitcoin.Utils;

namespace Atomex.Wallet.Tezos
{
    public class TezosExtKey : IExtKey, IDisposable
    {
        private static readonly byte[] HashKey = Encoders.ASCII.DecodeData(encoded: "ed25519 seed");

        private const int PrivateKeyLength = 32;
        private const int ChainCodeLength = 32;

        private readonly SecureBytes _privateKey;
        private readonly SecureBytes _publicKey;
        private byte[] ChainCode { get; }
        private uint Child { get; }
        private uint Depth { get; }
        private uint Fingerprint { get; }

        public TezosExtKey(SecureBytes seed)
        {
            var scopedSeed = seed.ToUnsecuredBytes();
            var scopedHashSeed = Hashes.HMACSHA512(HashKey, scopedSeed);
            using var secureHashSeed = new SecureBytes(scopedHashSeed);

            Ed25519.GenerateKeyPair(
                seed: secureHashSeed,
                privateKey: out _privateKey,
                publicKey: out _publicKey);

            ChainCode = new byte[ChainCodeLength];

            // copy hashSeed last 32 bytes to ChainCode
            Buffer.BlockCopy(
                src: scopedHashSeed,
                srcOffset: PrivateKeyLength,
                dst: ChainCode,
                dstOffset: 0,
                count: ChainCodeLength);
        }

        private TezosExtKey(
            SecureBytes privateKey,
            SecureBytes publicKey,
            byte depth,
            uint child,
            byte[] chainCode,
            uint fingerPrint)
        {
            _privateKey = privateKey;
            _publicKey = publicKey;

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
            Derive(
                chainCode: ChainCode,
                child: index,
                childChainCode: out var childChainCode,
                childPrivateKey: out var childPrivateKey,
                childPublicKey: out var childPublicKey);

            var scopedPublicKey = _publicKey.ToUnsecuredBytes();

            var fingerPrint = Utils.ToUInt32(
                value: Hashes.Hash160(scopedPublicKey).ToBytes(),
                littleEndian: true);

            return new TezosExtKey(
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
            var scopedPrivateKey = _privateKey.ToUnsecuredBytes();

            return TezosSigner.Sign(
                data: hash,
                privateKey: scopedPrivateKey);
        }

        public byte[] SignMessage(byte[] data)
        {
            var scopedPrivateKey = _privateKey.ToUnsecuredBytes();

            return TezosSigner.Sign(
                data: data,
                privateKey: scopedPrivateKey);
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

        private void Derive(
            byte[] chainCode,
            uint child,
            out byte[] childChainCode,
            out SecureBytes childPrivateKey,
            out SecureBytes childPublicKey)
        {
            var scopedPublicKey = _publicKey.ToUnsecuredBytes();
            var scopedPrivateKey = _privateKey.ToUnsecuredBytes();

            var data = new byte[1 + 32 + 4];

            if (child >> 31 == 0)
            {
                data[0] = 0;
                Buffer.BlockCopy(src: scopedPublicKey, srcOffset: 0, dst: data, dstOffset: 1, count: 32);
            }
            else // hardened key (private derivation)
            {
                data[0] = 0;
                Buffer.BlockCopy(src: scopedPrivateKey, srcOffset: 0, dst: data, dstOffset: 1, count: 32);
            }

            Buffer.BlockCopy(src: IndexToBytes(child), srcOffset: 0, dst: data, dstOffset: 33, count: 4);

            var l = Hashes.HMACSHA512(chainCode, data);
            var scopedChildPrivateKey = l.SubArray(start: 0, length: 32);

            childPrivateKey = new SecureBytes(scopedChildPrivateKey);

            childChainCode = new byte[ChainCodeLength];

            Buffer.BlockCopy(
                src: l,
                srcOffset: 32,
                dst: childChainCode,
                dstOffset: 0,
                count: ChainCodeLength);

            Ed25519.GeneratePublicKey(
                privateKey: childPrivateKey,
                publicKey: out childPublicKey);
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

        public void Dispose()
        {
            if (_privateKey != null)
                _privateKey.Dispose();

            if (_publicKey != null)
                _publicKey.Dispose();
        }
    }

}