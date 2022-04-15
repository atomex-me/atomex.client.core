using System;

using Atomex.Common.Libsodium;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography
{
    public class HmacSha256 : MacAlgorithm
    {
        private readonly MacAlgorithm _impl;

        public HmacSha256()
        {
            _impl = Sodium.IsInitialized
                ? (MacAlgorithm)new Libsodium.HmacSha256()
                : new DotNet.HmacSha256();
        }

        public override int HashSize => _impl.HashSize;

        public override byte[] Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data) => _impl.Mac(key, data);

        public override void Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            Span<byte> mac) => _impl.Mac(key, data, mac);

        public override bool Verify(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> mac) => _impl.Verify(key, data, mac);
    }
}