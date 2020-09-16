using System;
using System.Diagnostics;
using NetSha512 = System.Security.Cryptography.SHA512;
using NetIncrementalHash = System.Security.Cryptography.IncrementalHash;
using NetHashAlgorithmName = System.Security.Cryptography.HashAlgorithmName;

using Atomex.Cryptography.Abstract;

namespace Atomex.Cryptography.DotNet
{
    public class Sha512 : HashAlgorithm
    {
        public override int HashSize => 64;

        public override byte[] Hash(ReadOnlySpan<byte> data)
        {
            using var sha512 = NetSha512.Create();

#if NETSTANDARD2_1
            var hash = new byte[HashSize];

            if (!sha512.TryComputeHash(data, hash, out var written))
                Debug.Fail("Can't compute hash");

            return hash;      
#else
            return sha512.ComputeHash(data.ToArray());
#endif
        }

        public override void Hash(ReadOnlySpan<byte> data, Span<byte> hash)
        {
            using var sha512 = NetSha512.Create();

#if NETSTANDARD2_1
            if (!sha512.TryComputeHash(data, hash, out var written))
                Debug.Fail("Can't compute hash");
#else
            sha512.ComputeHash(data.ToArray()).CopyTo(hash);
#endif
        }

        public unsafe override bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> hash)
        {
            Debug.Assert(hash.Length == HashSize);

            using var sha512 = NetSha512.Create();

#if NETSTANDARD2_1
            var temp = stackalloc byte[HashSize];
            var tempSpan = new Span<byte>(temp, HashSize);

            if (!sha512.TryComputeHash(data, tempSpan, out var written))
                Debug.Fail("Can't compute hash");
#else
            var tempHash = sha512.ComputeHash(data.ToArray());
            fixed (byte* temp = tempHash)
#endif
            fixed (byte* @out = hash)
                return FixedTimeEqual.Equals(temp, @out, hash.Length);
        }

        public override IIncrementalHash CreateIncrementalHash() => new Sha512Incremental();
    }

    public class Sha512Incremental : IIncrementalHash
    {
        private readonly NetIncrementalHash _sha512;

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
#if NETSTANDARD2_1
            _sha512.AppendData(data);
#else
            _sha512.AppendData(data.ToArray());
#endif
        }

        public void Finalize(Span<byte> hash)
        {
#if NETSTANDARD2_1
            if (!_sha512.TryGetHashAndReset(hash, out _))
                Debug.Fail("Can't finalize hash");
#else
            _sha512.GetHashAndReset().CopyTo(hash);
#endif
        }

        public byte[] Finalize()
        {
            return _sha512.GetHashAndReset();
        }

        public void Dispose()
        {
            _sha512.Dispose();
        }
    }
}