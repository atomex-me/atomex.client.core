using System;

using Atomex.Common.Libsodium;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography
{
    public class HmacBlake2b : MacAlgorithm
    {
        public static readonly int DefaultKeySize = 32;
        public static readonly int DefaultMacSize = 32;

        private readonly MacAlgorithm _impl;

        public HmacBlake2b()
            : this(DefaultKeySize, DefaultMacSize)
        {
        }

        public HmacBlake2b(int keySize, int macSize)
        {
            _impl = Sodium.IsInitialized
                ? (MacAlgorithm)new Libsodium.HmacBlake2b(keySize, macSize)
                : new BouncyCastle.HmacBlake2b(keySize, macSize);
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