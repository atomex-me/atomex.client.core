using System;
using System.Threading.Tasks;

using Xunit;

using Atomex.Common.Memory;
using Atomex.Common.Libsodium;
using Atomex.Cryptography.Abstract;

namespace Atomex.Common
{
    public class UnmanagedBytesTests
    {
        public UnmanagedBytesTests()
        {
            Sodium.Initialize();
        }

        [Fact]
        public void CanBeAllocated()
        {
            using var unmanagedBytes = new UnmanagedBytes(10);

            Assert.NotNull(unmanagedBytes);
            Assert.Equal(10, unmanagedBytes.Length);
        }

        [Fact]
        public void CanAllocate10Mb()
        {
            const int size = 1024 * 1024 * 10;

            using var unmanagedBytes = new UnmanagedBytes(size);

            Assert.NotNull(unmanagedBytes);
            Assert.Equal(size, unmanagedBytes.Length);
        }

        [Fact]
        public void CanBeCreatedFromArray()
        {
            var src = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            using var unmanagedBytes = new UnmanagedBytes(src);

            Assert.NotNull(unmanagedBytes);
            Assert.Equal(src.Length, unmanagedBytes.Length);
            Assert.Equal(src, unmanagedBytes.ToBytes());
        }

        [Fact]
        public void CanGetSpan()
        {
            var src = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            using var unmanagedBytes = new UnmanagedBytes(src);

            var span = unmanagedBytes.GetSpan();
            Assert.Equal(src, span.ToArray());

            span[1] = 0x05;
            Assert.Equal(span.ToArray(), unmanagedBytes.ToBytes());
        }

        [Fact]
        public void CanGetReadOnlySpan()
        {
            var src = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            using var unmanagedBytes = new UnmanagedBytes(src);

            Assert.Equal(src, unmanagedBytes.GetReadOnlySpan().ToArray());
        }

        [Fact]
        public void CanGetMemory()
        {
            var src = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            using var unmanagedBytes = new UnmanagedBytes(src);

            var memory = unmanagedBytes.GetMemory();
            Assert.Equal(src, memory.ToArray());

            memory.Span[1] = 0x05;
            Assert.Equal(memory.ToArray(), unmanagedBytes.GetMemory().ToArray());
        }

        [Fact]
        public void CanGetReadOnlyMemory()
        {
            var src = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            using var unmanagedBytes = new UnmanagedBytes(src);

            Assert.Equal(src, unmanagedBytes.GetReadOnlyMemory().ToArray());
        }

        [Fact]
        public void CanBeCopiedToSpan()
        {
            var src = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            using var unmanagedBytes = new UnmanagedBytes(src);

            var dst = new byte[src.Length];
            var dstSpan = new Span<byte>(dst);

            unmanagedBytes.CopyTo(dstSpan);

            Assert.Equal(src, dst);
        }

        [Fact]
        public void CanParallelAllocLotsOfUnmanagedBytes()
        {
            const int AllocPerTask = 10000;

            var result = Parallel.For(0, 4, iteration =>
            {
                var startFrom = iteration * AllocPerTask;

                for (var i = startFrom; i < startFrom + AllocPerTask; ++i)
                {
                    var src = HashAlgorithm.Sha256.Hash(BitConverter.GetBytes(i));

                    using var unmanagedBytes = new UnmanagedBytes(src);

                    var dst = unmanagedBytes.ToBytes();

                    Assert.Equal(src, dst);
                }
            });
        }
    }
}