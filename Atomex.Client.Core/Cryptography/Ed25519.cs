//using System;

//using Atomex.Common.Memory;
//using Atomex.Cryptography.Abstract;
//using Atomex.Cryptography.BouncyCastle;

//namespace Atomex.Cryptography
//{
//    public class Ed25519 : SignatureAlgorithm
//    {
//        public int SignatureSize = 64;

//        public override byte[] Sign(
//            byte[] key,
//            byte[] data)
//        {
//            return Sign(
//                new ReadOnlySpan<byte>(key),
//                new ReadOnlySpan<byte>(data));
//        }

//        public override byte[] Sign(
//            ReadOnlySpan<byte> key,
//            ReadOnlySpan<byte> data)
//        {
//            var signature = new byte[SignatureSize];

//            Sign(key, data, signature);

//            return signature;
//        }

//        public override byte[] Sign(
//            SecureBytes key,
//            ReadOnlySpan<byte> data)
//        {
//            using var scopedKey = key.ToBytes();

//            return Sign(scopedKey.GetReadOnlySpan(), data);
//        }

//        public override void Sign(
//            ReadOnlySpan<byte> key,
//            ReadOnlySpan<byte> data,
//            Span<byte> signature)
//        {
//            BcEd25519.Sign(key, data, signature);
//        }

//        public override void Sign(
//            SecureBytes key,
//            ReadOnlySpan<byte> data,
//            Span<byte> signature)
//        {
//            using var scopedKey = key.ToBytes();

//            Sign(scopedKey.GetReadOnlySpan(), data, signature);
//        }

//        public byte[] SignWithExtendedKey(
//            byte[] extendedKey,
//            byte[] data)
//        {
//            return SignWithExtendedKey(
//                new ReadOnlySpan<byte>(extendedKey),
//                new ReadOnlySpan<byte>(data));
//        }

//        public byte[] SignWithExtendedKey(
//            ReadOnlySpan<byte> extendedKey,
//            ReadOnlySpan<byte> data)
//        {
//            var signature = new byte[SignatureSize];

//            SignWithExtendedKey(extendedKey, data, signature);

//            return signature;
//        }

//        public byte[] SignWithExtendedKey(
//            SecureBytes extendedKey,
//            ReadOnlySpan<byte> data)
//        {
//            using var scopedKey = extendedKey.ToBytes();

//            return SignWithExtendedKey(scopedKey.GetReadOnlySpan(), data);
//        }

//        public void SignWithExtendedKey(
//            ReadOnlySpan<byte> key,
//            ReadOnlySpan<byte> data,
//            Span<byte> signature)
//        {
//            BcEd25519.SignWithExtendedKey(key, data, signature);
//        }

//        public void SignWithExtendedKey(
//            SecureBytes key,
//            ReadOnlySpan<byte> data,
//            Span<byte> signature)
//        {
//            using var scopedKey = key.ToBytes();

//            SignWithExtendedKey(scopedKey.GetReadOnlySpan(), data, signature);
//        }

//        public override bool Verify(
//            byte[] publicKey,
//            byte[] data,
//            byte[] signature)
//        {
//            return Verify(
//                new ReadOnlySpan<byte>(publicKey),
//                new ReadOnlySpan<byte>(data),
//                new ReadOnlySpan<byte>(signature));
//        }

//        public override bool Verify(
//            ReadOnlySpan<byte> publicKey,
//            ReadOnlySpan<byte> data,
//            ReadOnlySpan<byte> signature)
//        {
//            return BcEd25519.Verify(publicKey, data, signature);
//        }
//    }
//}