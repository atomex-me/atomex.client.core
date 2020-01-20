using Atomex.Common;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class SecureBytesTests
    {
        [Fact]
        public void FromBytesArrayTest()
        {
            const string Phrase =
                "return auction present awesome blast excess receive obtain explain spider iron hip curtain recipe tent aim bonus hip cliff shrug lyrics pass right spend";

            var seed = new Mnemonic(Phrase).DeriveSeed();

            using var secureSeed = new SecureBytes(seed);
            using var scopedSeed = secureSeed.ToUnsecuredBytes();

            Assert.Equal(seed, scopedSeed);
        }

        [Fact]
        public void CloneTest()
        {
            var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };

            using var secureBytes = new SecureBytes(bytes);
            using var clone = secureBytes.Clone();
            using var cloneBytes = clone.ToUnsecuredBytes();

            Assert.Equal(bytes, cloneBytes);
        }
    }
}