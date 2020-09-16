using System;
using System.Diagnostics;
using System.Security.Cryptography;

using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography.DotNet
{
    public class HmacSha512 : MacAlgorithm
    {
        public override int HashSize => 64;

        public override byte[] Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data)
        {
            using var hmacSha512 = new HMACSHA512(key.ToArray());

#if NETSTANDARD2_1
            var hash = new byte[HashSize];

            if (!hmacSha512.TryComputeHash(data, hash, out var written))
                Debug.Fail("Can't compute hash");

            return hash;
#else
            return hmacSha512.ComputeHash(data.ToArray());
#endif
        }

        public override void Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            Span<byte> mac)
        {
            using var hmacSha512 = new HMACSHA512(key.ToArray());

#if NETSTANDARD2_1
            if (!hmacSha512.TryComputeHash(data, mac, out var written))
                Debug.Fail("Can't compute hash");
#else
            hmacSha512.ComputeHash(data.ToArray()).CopyTo(mac);
#endif
        }

        public unsafe override bool Verify(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> mac)
        {
            using var hmacSha512 = new HMACSHA512(key.ToArray());

#if NETSTANDARD2_1
            var temp = stackalloc byte[HashSize];
            var tempSpan = new Span<byte>(temp, HashSize);

            if (!hmacSha512.TryComputeHash(data, tempSpan, out var written))
                Debug.Fail("Can't compute hash");
#else
            var tempHash = hmacSha512.ComputeHash(data.ToArray());
            fixed (byte* temp = tempHash)
#endif
            fixed (byte* @out = mac)
                return FixedTimeEqual.Equals(temp, @out, mac.Length);
        }
    }
}