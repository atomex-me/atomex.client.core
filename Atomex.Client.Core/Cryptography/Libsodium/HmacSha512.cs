using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

using Atomex.Cryptography.Abstract;
using static Atomex.Common.Libsodium.Interop.Libsodium;

namespace Atomex.Cryptography.Libsodium
{
    public class HmacSha512 : MacAlgorithm
    {
        public override int HashSize => crypto_auth_hmacsha512_BYTES;

        public override byte[] Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data)
        {
            var temp = new byte[crypto_auth_hmacsha512_BYTES];

            Mac(key, data, temp);

            return temp;
        }

        public unsafe override void Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            Span<byte> mac)
        {
            //Debug.Assert(key.Length == crypto_hash_sha512_BYTES);
            Debug.Assert(mac.Length <= crypto_auth_hmacsha512_BYTES);

            var temp = stackalloc byte[crypto_auth_hmacsha512_BYTES];

            fixed (byte* @in = data)
            fixed (byte* k = key)
            {
                crypto_auth_hmacsha512_state state;

                crypto_auth_hmacsha512_init(&state, k, (UIntPtr)key.Length);
                crypto_auth_hmacsha512_update(&state, @in, (ulong)data.Length);
                crypto_auth_hmacsha512_final(&state, temp);
            }

            fixed (byte* @out = mac)
                Unsafe.CopyBlockUnaligned(@out, temp, (uint)mac.Length);
        }

        public unsafe override bool Verify(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> mac)
        {
            //Debug.Assert(key.Length == crypto_hash_sha512_BYTES);
            Debug.Assert(mac.Length <= crypto_auth_hmacsha512_BYTES);

            var temp = stackalloc byte[crypto_auth_hmacsha512_BYTES];

            fixed (byte* @in = data)
            fixed (byte* k = key)
            {
                crypto_auth_hmacsha512_state state;

                crypto_auth_hmacsha512_init(&state, k, (UIntPtr)key.Length);
                crypto_auth_hmacsha512_update(&state, @in, (ulong)data.Length);
                crypto_auth_hmacsha512_final(&state, temp);
            }

            fixed (byte* @out = mac)
                return FixedTimeEqual.Equals(temp, @out, mac.Length);
        }
    }
}