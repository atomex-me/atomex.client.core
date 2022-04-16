﻿using System;
using System.Threading.Tasks;

using Xunit;

using Atomex.Common.Libsodium;
using Atomex.Common.Memory;
using Atomex.Cryptography.Abstract;

namespace Atomex.Common
{
    public class SecureBytesTests
    {
        public SecureBytesTests()
        {
            Sodium.Initialize();
        }

        [Fact]
        public void CanBeCreatedFromSpan()
        {
            var src = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            using var secureBytes = new SecureBytes(src);

            Assert.NotNull(secureBytes);
            Assert.Equal(src.Length, secureBytes.Length);
        }

        [Fact]
        public void CanBeCreateFormUnmanagedBytes()
        {
            var src = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            using var unmanagedBytes = new UnmanagedBytes(src);

            using var secureBytes = new SecureBytes(unmanagedBytes);

            Assert.NotNull(secureBytes);
            Assert.Equal(src.Length, secureBytes.Length);
            Assert.Equal(src, secureBytes.ToUnsecuredBytes());
        }

        [Fact]
        public void CanGetUnsecuredBytes()
        {
            var src = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            using var secureBytes = new SecureBytes(src);

            Assert.Equal(src, secureBytes.ToUnsecuredBytes());
        }

        [Fact]
        public void CanBeReseted()
        {
            var src = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            using var secureBytes = new SecureBytes(src);

            var src1 = new byte[] { 0x05, 0x04, 0x03, 0x02, 0x01 };

            secureBytes.Reset(src1);

            Assert.Equal(src1, secureBytes.ToUnsecuredBytes());
        }

        [Fact]
        public void CanBeCopied()
        {
            var src = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            using var secureBytes = new SecureBytes(src);
            using var secureBytes2 = secureBytes.Copy();

            Assert.Equal(src, secureBytes2.ToUnsecuredBytes());

            secureBytes.Reset(new byte[] { 0x01 });

            Assert.Equal(src, secureBytes2.ToUnsecuredBytes());
        }

        [Fact]
        public void CanParallelAllocLotsOfSecureBytes()
        {
            const int AllocPerTask = 1000;

            var result = Parallel.For(0, 4, iteration =>
            {
                var startFrom = iteration * AllocPerTask;

                for (var i = startFrom; i < startFrom + AllocPerTask; ++i)
                {
                    var src = HashAlgorithm.Sha256.Hash(BitConverter.GetBytes(i));

                    using var secureBytes = new SecureBytes(src);

                    var dst = secureBytes.ToUnsecuredBytes();

                    Assert.Equal(src, dst);
                }
            });
        }
    }
}