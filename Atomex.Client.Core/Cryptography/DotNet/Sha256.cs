using System;
using System.Diagnostics;
using NetSha256 = System.Security.Cryptography.SHA256;
using NetIncrementalHash = System.Security.Cryptography.IncrementalHash;
using NetHashAlgorithmName = System.Security.Cryptography.HashAlgorithmName;

using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography.DotNet
{
    public class Sha256 : HashAlgorithm
    {
        public override int HashSize => 32;

        public override byte[] Hash(ReadOnlySpan<byte> data)
        {
            using var sha256 = NetSha256.Create();

#if NETSTANDARD2_1
            var hash = new byte[HashSize];

            if (!sha256.TryComputeHash(data, hash, out var written))
                Debug.Fail("Can't compute hash");

            return hash;      
#else
            return sha256.ComputeHash(data.ToArray());
#endif
        }

        public override void Hash(ReadOnlySpan<byte> data, Span<byte> hash)
        {
            using var sha256 = NetSha256.Create();

#if NETSTANDARD2_1
            if (!sha256.TryComputeHash(data, hash, out var written))
                Debug.Fail("Can't compute hash");
#else
            sha256.ComputeHash(data.ToArray()).CopyTo(hash);
#endif
        }

        public unsafe override bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> hash)
        {
            Debug.Assert(hash.Length == HashSize);

            using var sha256 = NetSha256.Create();

#if NETSTANDARD2_1
            var temp = stackalloc byte[HashSize];
            var tempSpan = new Span<byte>(temp, HashSize);

            if (!sha256.TryComputeHash(data, tempSpan, out var written))
                Debug.Fail("Can't compute hash");
#else
            var tempHash = sha256.ComputeHash(data.ToArray());
            fixed (byte* temp = tempHash)
#endif
            fixed (byte* @out = hash)
                return FixedTimeEqual.Equals(temp, @out, hash.Length);
        }

        public override IIncrementalHash CreateIncrementalHash() => new Sha256Incremental();
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
#if NETSTANDARD2_1
            _sha256.AppendData(data);
#else
            _sha256.AppendData(data.ToArray());
#endif
        }

        public void Finalize(Span<byte> hash)
        {
#if NETSTANDARD2_1
            if (!_sha256.TryGetHashAndReset(hash, out _))
                Debug.Fail("Can't finalize hash");
#else
            _sha256.GetHashAndReset().CopyTo(hash);
#endif
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