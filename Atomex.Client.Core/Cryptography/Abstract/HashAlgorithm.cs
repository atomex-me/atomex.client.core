using System;
using System.Threading;

namespace Atomex.Cryptography.Abstract
{
    public abstract class HashAlgorithm
    {
        private static Sha256 _sha256;
        public static Sha256 Sha256
        {
            get
            {
                var instance = _sha256;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _sha256, new Sha256(), null);
                    instance = _sha256;
                }

                return instance;
            }
        }

        private static Sha512 _sha512;
        public static Sha512 Sha512
        {
            get {
                var instance = _sha512;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _sha512, new Sha512(), null);
                    instance = _sha512;
                }

                return instance;
            }
        }

        public abstract int HashSize { get; }

        public abstract byte[] Hash(ReadOnlySpan<byte> data);

        public abstract void Hash(ReadOnlySpan<byte> data, Span<byte> hash);

        public abstract bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> hash);

        public abstract IIncrementalHash CreateIncrementalHash();
    }
}