using System;
using System.Diagnostics;
using System.Security.Cryptography;

using Atomex.Common;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography.DotNet
{
    public class HmacSha256 : MacAlgorithm
    {
        public override int HashSize => 32;

        public override byte[] Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data)
        {
            using var hmacSha256 = new HMACSHA256(key.ToArray());

            var hash = new byte[HashSize];

            if (!hmacSha256.TryComputeHash(data, hash, out var written))
                throw new Exception("Can't compute hash");

            return hash;
        }

        public override void Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            Span<byte> mac)
        {
            using var hmacSha256 = new HMACSHA256(key.ToArray());

            if (!hmacSha256.TryComputeHash(data, mac, out var written))
                throw new Exception("Can't compute hash");
        }

        public unsafe override bool Verify(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> mac)
        {
            using var hmacSha256 = new HMACSHA256(key.ToArray());

            var temp = stackalloc byte[HashSize];
            var tempSpan = new Span<byte>(temp, HashSize);

            if (!hmacSha256.TryComputeHash(data, tempSpan, out var written))
                throw new Exception("Can't compute hash");

            fixed (byte* @out = mac)
                return FixedTimeEqual.Equals(temp, @out, mac.Length);
        }
    }
}