using System;
using System.Diagnostics;

using Atomex.Cryptography.Abstract;
using static Atomex.Common.Libsodium.Interop.Libsodium;

namespace Atomex.Cryptography.Libsodium
{
    public class Sha512 : HashAlgorithm
    {
        public override int HashSize => crypto_hash_sha512_BYTES;

        public override byte[] Hash(ReadOnlySpan<byte> data)
        {
            var hash = new byte[crypto_hash_sha512_BYTES];

            Hash(data, hash);

            return hash;
        }

        public unsafe override void Hash(ReadOnlySpan<byte> data, Span<byte> hash)
        {
            Debug.Assert(hash.Length == crypto_hash_sha512_BYTES);

            fixed (byte* @out = hash)
            fixed (byte* @in = data)
            {
                var error = crypto_hash_sha512(@out, @in, (ulong)data.Length);

                Debug.Assert(error == 0);
            }
        }

        public unsafe override bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> hash)
        {
            Debug.Assert(hash.Length == crypto_hash_sha512_BYTES);

            var temp = stackalloc byte[crypto_hash_sha512_BYTES];

            fixed (byte* @in = data)
            {
                var error = crypto_hash_sha512(temp, @in,(ulong)data.Length);

                Debug.Assert(error == 0);
            }

            fixed (byte* @out = hash)
                return FixedTimeEqual.Equals(temp, @out, hash.Length);
        }

        public override IIncrementalHash CreateIncrementalHash() => new Sha512Incremental();
    }

    public class Sha512Incremental : IIncrementalHash
    {
        private readonly crypto_hash_sha512_state _state;

        public unsafe Sha512Incremental()
        {
            Initialize();
        }

        public unsafe void Initialize()
        {
            fixed (crypto_hash_sha512_state* state = &_state)
            {
                var error = crypto_hash_sha512_init(state);

                Debug.Assert(error == 0);
            }
        }

        public unsafe void Update(ReadOnlySpan<byte> data)
        {
            fixed (crypto_hash_sha512_state* state = &_state)
            fixed (byte* @in = data)
            {
                var error = crypto_hash_sha512_update(state, @in, (ulong)data.Length);

                Debug.Assert(error == 0);
            }
        }

        public unsafe void Finalize(Span<byte> hash)
        {
            Debug.Assert(hash.Length == crypto_hash_sha512_BYTES);

            fixed (crypto_hash_sha512_state* state = &_state)
            fixed (byte* @out = hash)
            {
                var error = crypto_hash_sha512_final(state, @out);

                Debug.Assert(error == 0);
            }
        }

        public byte[] Finalize()
        {
            var hash = new byte[crypto_hash_sha512_BYTES];

            Finalize(hash);

            return hash;
        }

        public void Dispose()
        {
            // nothing to do
        }
    }
}