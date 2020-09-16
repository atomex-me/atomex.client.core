using System;
using System.Threading;

namespace Atomex.Cryptography.Abstract
{
    public abstract class MacAlgorithm
    {
        private static HmacSha256 _hmacSha256;
        public static HmacSha256 HmacSha256
        {
            get {
                var instance = _hmacSha256;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _hmacSha256, new HmacSha256(), null);
                    instance = _hmacSha256;
                }

                return instance;
            }
        }

        private static HmacSha512 _hmacSha512;
        public static HmacSha512 HmacSha512
        {
            get {
                var instance = _hmacSha512;

                if (instance == null)
                {
                    Interlocked.CompareExchange(ref _hmacSha512, new HmacSha512(), null);
                    instance = _hmacSha512;
                }

                return instance;
            }
        }

        public abstract int HashSize { get; }

        public abstract byte[] Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data);

        public abstract void Mac(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            Span<byte> mac);

        public abstract bool Verify(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> mac);
    }
}