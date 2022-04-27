using System;
using System.Diagnostics;

using Atomex.Common;
using Atomex.Cryptography.Abstract;
using static Atomex.Common.Libsodium.Interop.Libsodium;

namespace Atomex.Cryptography.Libsodium
{
    public class Blake2b : HashAlgorithm
    {
        public static readonly int MinHashSize = 20;
        public static readonly int MaxHashSize = crypto_generichash_blake2b_BYTES_MAX;

        private readonly int _hashSize;

        public override int HashSize => _hashSize;

        public Blake2b()
            : this(hashSize: crypto_generichash_blake2b_BYTES)
        {
        }

        public Blake2b(int hashSize)
        {
            if (hashSize < MinHashSize || hashSize > MaxHashSize)
                throw new ArgumentOutOfRangeException(nameof(hashSize), $"Hash size must be between {MinHashSize} and {MaxHashSize} bytes");

            _hashSize = hashSize;
        }

        public override byte[] Hash(ReadOnlySpan<byte> data)
        {
            var hash = new byte[HashSize];

            Hash(data, hash);

            return hash;
        }

        public unsafe override void Hash(
            ReadOnlySpan<byte> data,
            Span<byte> hash)
        {
            fixed (byte* @out = hash)
            fixed (byte* @in = data)
            {
                var error = crypto_generichash_blake2b(
                    @out,
                    (UIntPtr)hash.Length,
                    @in,
                    (ulong)data.Length,
                    IntPtr.Zero,
                    0);

                Debug.Assert(error == 0);
            }
        }

        public unsafe override bool Verify(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> hash)
        {
            var temp = stackalloc byte[HashSize];

            fixed (byte* @in = data)
            {
                var error = crypto_generichash_blake2b(
                    temp,
                    (UIntPtr)hash.Length,
                    @in,
                    (ulong)data.Length,
                    IntPtr.Zero,
                    0);

                Debug.Assert(error == 0);
            }

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
        private readonly crypto_generichash_blake2b_state _state;

        public int HashSize { get; private set; }

        public Blake2bIncremental()
            : this(hashSize: crypto_generichash_blake2b_BYTES)
        {
        }

        public Blake2bIncremental(int hashSize)
        {
            HashSize = hashSize;

            Initialize();
        }

        public unsafe void Initialize()
        {
            fixed (crypto_generichash_blake2b_state* state = &_state)
            {
                var error = crypto_generichash_blake2b_init(
                    state,
                    IntPtr.Zero,
                    0,
                    (UIntPtr) HashSize);

                Debug.Assert(error == 0);
            }
        }

        public unsafe void Update(ReadOnlySpan<byte> data)
        {
            fixed (crypto_generichash_blake2b_state* state = &_state)
            fixed (byte* @in = data)
            {
                var error = crypto_generichash_blake2b_update(
                    state,
                    @in,
                    (ulong)data.Length);

                Debug.Assert(error == 0);
            }
        }

        public unsafe void Finalize(Span<byte> hash)
        {
            fixed (crypto_generichash_blake2b_state* state = &_state)
            fixed (byte* @out = hash)
            {
                var error = crypto_generichash_blake2b_final(
                    state,
                    @out,
                    (UIntPtr)hash.Length);

                Debug.Assert(error == 0);
            }
        }

        public byte[] Finalize()
        {
            var hash = new byte[HashSize];

            Finalize(hash);

            return hash;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}