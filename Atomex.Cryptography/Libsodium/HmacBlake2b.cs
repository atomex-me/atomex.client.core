using System;
using System.Runtime.CompilerServices;

using Atomex.Common;
using Atomex.Cryptography.Abstract;
using static Atomex.Common.Libsodium.Interop.Libsodium;

namespace Atomex.Cryptography.Libsodium
{
    public class HmacBlake2b : MacAlgorithm
    {
        public static readonly int MinKeySize = crypto_generichash_blake2b_KEYBYTES_MIN;
        public static readonly int MaxKeySize = crypto_generichash_blake2b_KEYBYTES_MAX;
        public static readonly int MinMacSize = crypto_generichash_blake2b_BYTES_MIN;
        public static readonly int MaxMacSize = crypto_generichash_blake2b_BYTES_MAX;

        private readonly int _keySize;
        private readonly int _macSize;

        public int KeySize => _keySize;
        public override int HashSize => _macSize;

        public HmacBlake2b() : this(
            keySize: crypto_generichash_blake2b_KEYBYTES,
            macSize: crypto_generichash_blake2b_BYTES)
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
            var temp = new byte[HashSize];

            Mac(key, data, temp);

            return temp;
        }

        public unsafe override void Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            Span<byte> mac)
        {
            var temp = stackalloc byte[HashSize];

            fixed (byte* @in = data)
            fixed (byte* k = key)
            {
                crypto_generichash_blake2b_state state;

                crypto_generichash_blake2b_init(&state, (IntPtr)k, (UIntPtr)key.Length, (UIntPtr)mac.Length);
                crypto_generichash_blake2b_update(&state, @in, (ulong)data.Length);
                crypto_generichash_blake2b_final(&state, temp, (UIntPtr)mac.Length);
            }

            fixed (byte* @out = mac)
                Unsafe.CopyBlockUnaligned(@out, temp, (uint)mac.Length);
        }

        public unsafe override bool Verify(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> mac)
        {
            var temp = stackalloc byte[HashSize];

            fixed (byte* @in = data)
            fixed (byte* k = key)
            {
                crypto_generichash_blake2b_state state;

                crypto_generichash_blake2b_init(&state, (IntPtr)k, (UIntPtr)key.Length, (UIntPtr)mac.Length);
                crypto_generichash_blake2b_update(&state, @in, (ulong)data.Length);
                crypto_generichash_blake2b_final(&state, temp, (UIntPtr)mac.Length);
            }

            fixed (byte* @out = mac)
                return FixedTimeEqual.Equals(temp, @out, mac.Length);
        }
    }
}