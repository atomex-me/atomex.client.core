using System;

using Atomex.Common.Libsodium;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography
{
    public class Sha256 : HashAlgorithm
    {
        private readonly HashAlgorithm _impl;

        public Sha256()
        {
            _impl = Sodium.IsInitialized
                ? new Libsodium.Sha256()
                : new DotNet.Sha256();
        }

        public override int HashSize => _impl.HashSize;

        public override byte[] Hash(
            ReadOnlySpan<byte> data) =>
            _impl.Hash(data);

        public override void Hash(
            ReadOnlySpan<byte> data,
            Span<byte> hash) =>
            _impl.Hash(data, hash);

        public override bool Verify(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> hash) =>
            _impl.Verify(data, hash);

        public override IIncrementalHash CreateIncrementalHash() =>
            _impl.CreateIncrementalHash();

        public override IIncrementalHash CreateIncrementalHash(int hashSize) =>
            _impl.CreateIncrementalHash(hashSize);

        public byte[] Hash(ReadOnlySpan<byte> data, int iterations)
        {
            var hash = Hash(data);

            for (var i = 1; i < iterations; ++i)
                hash = Hash(hash);

            return hash;
        }
    }
}