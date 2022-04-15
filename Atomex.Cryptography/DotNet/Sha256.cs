using System;
using System.Diagnostics;
using NetSha256 = System.Security.Cryptography.SHA256;
using NetIncrementalHash = System.Security.Cryptography.IncrementalHash;
using NetHashAlgorithmName = System.Security.Cryptography.HashAlgorithmName;

using Atomex.Common;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography.DotNet
{
    public class Sha256 : HashAlgorithm
    {
        public override int HashSize => 32;

        public override byte[] Hash(
            ReadOnlySpan<byte> data)
        {
            return NetSha256.HashData(data);
        }

        public override void Hash(
            ReadOnlySpan<byte> data,
            Span<byte> hash)
        {
            NetSha256.HashData(data, hash);
        }

        public unsafe override bool Verify(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> hash)
        {
            Debug.Assert(hash.Length == HashSize);

            var temp = stackalloc byte[HashSize];
            var tempHash = new Span<byte>(temp, HashSize);

            NetSha256.HashData(data, tempHash);

            fixed (byte* @out = hash)
                return FixedTimeEqual.Equals(temp, @out, hash.Length);
        }

        public override IIncrementalHash CreateIncrementalHash() => new Sha256Incremental();

        public override IIncrementalHash CreateIncrementalHash(int hashSize) =>
            hashSize == HashSize
                ? CreateIncrementalHash()
                : throw new NotSupportedException($"The hash size of the Sha256 is fixed and equal to {HashSize} bytes.");
    }

    public class Sha256Incremental : IIncrementalHash
    {
        private readonly NetIncrementalHash _sha256;

        public Sha256Incremental()
        {
            _sha256 = NetIncrementalHash.CreateHash(NetHashAlgorithmName.SHA256);
        }

        public void Initialize()
        {
            _sha256.GetHashAndReset();
        }

        public void Update(ReadOnlySpan<byte> data)
        {
            _sha256.AppendData(data);
        }

        public void Finalize(Span<byte> hash)
        {
            if (!_sha256.TryGetHashAndReset(hash, out _))
                Debug.Fail("Can't finalize hash");
        }

        public byte[] Finalize()
        {
            return _sha256.GetHashAndReset();
        }

        public void Dispose()
        {
            _sha256.Dispose();
        }
    }
}