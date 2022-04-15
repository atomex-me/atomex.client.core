using System;

using Atomex.Common.Libsodium;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography
{
    public class Sha512 : HashAlgorithm
    {
        private readonly HashAlgorithm _impl;

        public Sha512()
        {
            _impl = Sodium.IsInitialized
                ? (HashAlgorithm)new Libsodium.Sha512()
                : new DotNet.Sha512();
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