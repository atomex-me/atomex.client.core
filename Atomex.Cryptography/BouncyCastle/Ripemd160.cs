using System;

using Org.BouncyCastle.Crypto.Digests;

using Atomex.Common;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography.BouncyCastle
{
    public class Ripemd160 : HashAlgorithm
    {
        public override int HashSize => 20;

        public override byte[] Hash(
            ReadOnlySpan<byte> data)
        {
            var ripemd160 = new RipeMD160Digest();

            var hash = new byte[ripemd160.GetDigestSize()];

            ripemd160.BlockUpdate(data.ToArray(), 0, data.Length);
            ripemd160.DoFinal(hash, 0);

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
            new Ripemd160Incremental();

        public override IIncrementalHash CreateIncrementalHash(int hashSize) =>
            hashSize == HashSize
                ? CreateIncrementalHash()
                : throw new NotSupportedException($"The hash size of the Ripemd160 is fixed and equal to {HashSize} bytes");
    }

    public class Ripemd160Incremental : IIncrementalHash
    {
        private RipeMD160Digest _ripemd160;

        public Ripemd160Incremental()
        {
            Initialize();
        }

        public void Initialize()
        {
            _ripemd160 = new RipeMD160Digest();
        }

        public void Update(ReadOnlySpan<byte> data)
        {
            _ripemd160.BlockUpdate(data.ToArray(), 0, data.Length);
        }

        public void Finalize(Span<byte> hash)
        {
            Finalize().CopyTo(hash);
        }

        public byte[] Finalize()
        {
            var result = new byte[_ripemd160.GetDigestSize()];

            _ripemd160.DoFinal(result, 0);

            return result;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}