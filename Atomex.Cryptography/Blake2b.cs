using System;

using Atomex.Common.Libsodium;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography
{
    public class Blake2b : HashAlgorithm
    {
        public static readonly int DefaultHashSize = 32;

        private readonly HashAlgorithm _impl;

        public Blake2b()
            : this(DefaultHashSize)
        {
        }

        public Blake2b(int hashSize)
        {
            _impl = Sodium.IsInitialized
                ? (HashAlgorithm)new Libsodium.Blake2b(hashSize)
                : new BouncyCastle.Blake2b(hashSize);
        }

        public override int HashSize => _impl.HashSize;

        public override byte[] Hash(ReadOnlySpan<byte> data) =>
            _impl.Hash(data);

        public override void Hash(ReadOnlySpan<byte> data, Span<byte> hash) =>
            _impl.Hash(data, hash);

        public override bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> hash) =>
            _impl.Verify(data, hash);

        public override IIncrementalHash CreateIncrementalHash() =>
            _impl.CreateIncrementalHash();

        public override IIncrementalHash CreateIncrementalHash(int hashSize) =>
            _impl.CreateIncrementalHash(hashSize);
    }
}