using System;
using System.Diagnostics;
using NetSha512 = System.Security.Cryptography.SHA512;
using NetIncrementalHash = System.Security.Cryptography.IncrementalHash;
using NetHashAlgorithmName = System.Security.Cryptography.HashAlgorithmName;

using Atomex.Common;
using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography.DotNet
{
    public class Sha512 : HashAlgorithm
    {
        public override int HashSize => 64;

        public override byte[] Hash(
            ReadOnlySpan<byte> data)
        {
            return NetSha512.HashData(data);
        }

        public override void Hash(
            ReadOnlySpan<byte> data,
            Span<byte> hash)
        {
            NetSha512.HashData(data, hash);
        }

        public unsafe override bool Verify(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> hash)
        {
            Debug.Assert(hash.Length == HashSize);

            var temp = stackalloc byte[HashSize];
            var tempHash = new Span<byte>(temp, HashSize);

            NetSha512.HashData(data, tempHash);

            fixed (byte* @out = hash)
                return FixedTimeEqual.Equals(temp, @out, hash.Length);
        }

        public override IIncrementalHash CreateIncrementalHash() =>
            new Sha512Incremental();

        public override IIncrementalHash CreateIncrementalHash(int hashSize) =>
            hashSize == HashSize
                ? CreateIncrementalHash()
                : throw new NotSupportedException($"The hash size of the Sha512 is fixed and equal to {HashSize} bytes.");
    }

    public class Sha512Incremental : IIncrementalHash
    {
        private readonly NetIncrementalHash _sha512;
        private bool disposedValue;

        public Sha512Incremental()
        {
            _sha512 = NetIncrementalHash.CreateHash(NetHashAlgorithmName.SHA512);
        }

        public void Initialize()
        {
            _sha512.GetHashAndReset();
        }

        public void Update(ReadOnlySpan<byte> data)
        {
            _sha512.AppendData(data);
        }

        public void Finalize(Span<byte> hash)
        {
            if (!_sha512.TryGetHashAndReset(hash, out _))
                Debug.Fail("Can't finalize hash");
        }

        public byte[] Finalize()
        {
            return _sha512.GetHashAndReset();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _sha512.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}