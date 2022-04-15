using System;

using Org.BouncyCastle.Crypto.Digests;

using Atomex.Common;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography.BouncyCastle
{
    public class Blake2b : HashAlgorithm
    {
        public static readonly int DefaultHashSize = 32;

        private readonly int _hashSize;

        public override int HashSize => _hashSize;

        public Blake2b()
            : this(DefaultHashSize)
        {
        }

        public Blake2b(int hashSize)
        {
            _hashSize = hashSize;
        }

        public override byte[] Hash(
            ReadOnlySpan<byte> data)
        {
            var hash = new byte[HashSize];

            var blake2b = new Blake2bDigest(HashSize * 8);

            blake2b.BlockUpdate(data.ToArray(), 0, data.Length);
            blake2b.DoFinal(hash, 0);

            return hash;
        }

        public override void Hash(
            ReadOnlySpan<byte> data,
            Span<byte> hash)
        {
            Hash(data).CopyTo(hash);
        }

        public unsafe override bool Verify(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> hash)
        {
            var h = Hash(data);

            fixed (byte* temp = h)
            fixed (byte* @out = hash)
                return FixedTimeEqual.Equals(temp, @out, hash.Length);
        }

        public override IIncrementalHash CreateIncrementalHash() =>
            new Blake2bIncremental();

        public override IIncrementalHash CreateIncrementalHash(int hashSize) =>
            new Blake2bIncremental(hashSize);
    }

    public class Blake2bIncremental : IIncrementalHash
    {
        private Blake2bDigest _blake2b;

        public int HashSize { get; private set; }

        public Blake2bIncremental()
            : this(Blake2b.DefaultHashSize)
        {
        }

        public Blake2bIncremental(int hashSize)
        {
            HashSize = hashSize;

            Initialize();
        }

        public void Initialize()
        {
            _blake2b = new Blake2bDigest(HashSize * 8);
        }

        public void Update(ReadOnlySpan<byte> data)
        {
            _blake2b.BlockUpdate(data.ToArray(), 0, data.Length);
        }

        public void Finalize(Span<byte> hash)
        {
            Finalize().CopyTo(hash);
        }

        public byte[] Finalize()
        {
            var result = new byte[HashSize];

            _blake2b.DoFinal(result, 0);

            return result;
        }

        public void Dispose()
        {
            // nothing to do
        }
    }
}