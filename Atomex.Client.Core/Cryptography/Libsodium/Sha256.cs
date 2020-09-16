using System;
using System.Diagnostics;

using Atomex.Cryptography.Abstract;
using static Atomex.Common.Libsodium.Interop.Libsodium;

namespace Atomex.Cryptography.Libsodium
{
    public class Sha256 : HashAlgorithm
    {
        public override int HashSize => crypto_hash_sha256_BYTES;

        public override byte[] Hash(ReadOnlySpan<byte> data)
        {
            var hash = new byte[crypto_hash_sha256_BYTES];

            Hash(data, hash);

            return hash;
        }

        public unsafe override void Hash(ReadOnlySpan<byte> data, Span<byte> hash)
        {
            Debug.Assert(hash.Length == crypto_hash_sha256_BYTES);

            fixed (byte* @out = hash)
            fixed (byte* @in = data)
            {
                var error = crypto_hash_sha256(@out, @in, (ulong)data.Length);

                Debug.Assert(error == 0);
            }
        }

        public unsafe override bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> hash)
        {
            Debug.Assert(hash.Length == crypto_hash_sha256_BYTES);

            var temp = stackalloc byte[crypto_hash_sha256_BYTES];

            fixed (byte* @in = data)
            {
                var error = crypto_hash_sha256(temp, @in,(ulong)data.Length);

                Debug.Assert(error == 0);
            }

            fixed (byte* @out = hash)
                return FixedTimeEqual.Equals(temp, @out, hash.Length);
        }

        public override IIncrementalHash CreateIncrementalHash() => new Sha256Incremental();
    }

    public class Sha256Incremental : IIncrementalHash
    {
        private readonly crypto_hash_sha256_state _state;

        public unsafe Sha256Incremental()
        {
            Initialize();
        }

        public unsafe void Initialize()
        {
            fixed (crypto_hash_sha256_state* state = &_state)
            {
                var error = crypto_hash_sha256_init(state);

                Debug.Assert(error == 0);
            }
        }

        public unsafe void Update(ReadOnlySpan<byte> data)
        {
            fixed (crypto_hash_sha256_state* state = &_state)
            fixed (byte* @in = data)
            {
                var error = crypto_hash_sha256_update(state, @in, (ulong)data.Length);

                Debug.Assert(error == 0);
            }
        }

        public unsafe void Finalize(Span<byte> hash)
        {
            Debug.Assert(hash.Length == crypto_hash_sha256_BYTES);

            fixed (crypto_hash_sha256_state* state = &_state)
            fixed (byte* @out = hash)
            {
                var error = crypto_hash_sha256_final(state, @out);

                Debug.Assert(error == 0);
            }
        }

        public byte[] Finalize()
        {
            var hash = new byte[crypto_hash_sha256_BYTES];

            Finalize(hash);

            return hash;
        }

        public void Dispose()
        {
            // nothing to do
        }
    }
}