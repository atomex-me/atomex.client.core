using System;

using Org.BouncyCastle.Crypto.Digests;

using Atomex.Cryptography.Abstract;
using Atomex.Common;

namespace Atomex.Cryptography.BouncyCastle
{
    public class HmacBlake2b : MacAlgorithm
    {
        public static readonly int MinKeySize = 16;
        public static readonly int DefaultKeySize = 32;
        public static readonly int MaxKeySize = 64;
        public static readonly int MinMacSize = 16;
        public static readonly int DefaultMacSize = 32;
        public static readonly int MaxMacSize = 64;

        private readonly int _keySize;
        private readonly int _macSize;

        public int KeySize => _keySize;
        public override int HashSize => _macSize;

        public HmacBlake2b() : this(
            keySize: DefaultKeySize,
            macSize: DefaultMacSize)
        {
        }

        public HmacBlake2b(int keySize, int macSize)
        {
            if (keySize < MinKeySize || keySize > MaxKeySize)
                throw new ArgumentOutOfRangeException(nameof(keySize), $"Key size must be between {MinKeySize} and {MaxKeySize} bytes");

            if (macSize < MinMacSize || macSize > MaxMacSize)
                throw new ArgumentOutOfRangeException(nameof(macSize), $"Mac size must be between {MinMacSize} and {MaxMacSize} bytes");

            _keySize = keySize;
            _macSize = macSize;
        }

        public override byte[] Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data)
        {
            var blake2b = key == null || key.IsEmpty
                ? new Blake2bDigest(HashSize * 8)
                : new Blake2bDigest(key.ToArray(), HashSize, null, null);

            var hash = new byte[blake2b.GetDigestSize()];
            blake2b.BlockUpdate(data.ToArray(), 0, data.Length);
            blake2b.DoFinal(hash, 0);

            return hash;
        }

        public override void Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            Span<byte> mac)
        {
            Mac(key, data).CopyTo(mac);
        }

        public unsafe override bool Verify(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> mac)
        {
            var hash = Mac(key, data);

            fixed (byte* temp = hash)
            fixed (byte* @out = mac)
                return FixedTimeEqual.Equals(temp, @out, mac.Length);
        }
    }
}