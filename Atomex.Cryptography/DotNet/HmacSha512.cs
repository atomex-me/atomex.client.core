using System;
using System.Diagnostics;
using System.Security.Cryptography;

using Atomex.Common;
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

            var hash = new byte[HashSize];

            if (!hmacSha512.TryComputeHash(data, hash, out var written))
                Debug.Fail("Can't compute hash");

            return hash;
        }

        public override void Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            Span<byte> mac)
        {
            using var hmacSha512 = new HMACSHA512(key.ToArray());

            if (!hmacSha512.TryComputeHash(data, mac, out var written))
                Debug.Fail("Can't compute hash");
        }

        public unsafe override bool Verify(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> mac)
        {
            using var hmacSha512 = new HMACSHA512(key.ToArray());

            var temp = stackalloc byte[HashSize];
            var tempSpan = new Span<byte>(temp, HashSize);

            if (!hmacSha512.TryComputeHash(data, tempSpan, out var written))
                Debug.Fail("Can't compute hash");

            fixed (byte* @out = mac)
                return FixedTimeEqual.Equals(temp, @out, mac.Length);
        }
    }
}