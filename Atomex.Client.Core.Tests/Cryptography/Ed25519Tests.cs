using System.Text;
using Xunit;

using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Cryptography.BouncyCastle;

namespace Atomex.Client.Core.Tests.Cryptography
{
    public class Ed25519Tests
    {
        private const int SignatureSize = 64;

        [Fact]
        public void CanCreateKeyPair()
        {
            for (var i = 32; i < 100; ++i)
            {
                var secret = Rand.SecureRandomBytes(i);

                BcEd25519.GenerateKeyPair(
                    new SecureBytes(secret),
                    out var privateKey,
                    out var publicKey);

                Assert.NotNull(privateKey);
                Assert.NotNull(publicKey);
            }
        }

        [Fact]
        public void CanGeneratePublicKey()
        {
            for (var i = 32; i < 100; ++i)
            {
                var secret = Rand.SecureRandomBytes(i);

                BcEd25519.GenerateKeyPair(
                    new SecureBytes(secret),
                    out var privateKey,
                    out var exptectedPublicKey);

                BcEd25519.GeneratePublicKey(
                    privateKey,
                    out var publicKey);

                Assert.NotNull(privateKey);
                Assert.NotNull(exptectedPublicKey);
                Assert.NotNull(publicKey);
                Assert.Equal(exptectedPublicKey.ToUnsecuredBytes(), publicKey.ToUnsecuredBytes());
            }
        }

        [Fact]
        public void CanGeneratePublicKeyFromExtendedKey()
        {
            for (var i = 64; i < 100; ++i)
            {
                var secret = Rand.SecureRandomBytes(i);

                // prune secret
                secret[0] &= 0b11111000;  // the lowest 3 bits kL are cleared (&= 0xF8)
                secret[31] &= 0b01111111; // the highest bit kL is cleared (&= 7F)
                secret[31] |= 0b01000000; // the second highest bit kL is set (|= 0x40)

                BcEd25519.GeneratePublicKeyFromExtended(
                    new SecureBytes(secret),
                    out var publicKey);

                Assert.NotNull(publicKey);
            }
        }

        [Fact]
        public void CanSignAndVerify()
        {
            for (var i = 32; i < 100; ++i)
            {
                var secret = Rand.SecureRandomBytes(32);

                BcEd25519.GenerateKeyPair(
                    new SecureBytes(secret),
                    out var privateKey,
                    out var publicKey);

                using var unmanagedPrivateKey = privateKey.ToUnmanagedBytes();
                using var unmanagedPublicKey = publicKey.ToUnmanagedBytes();

                var data = Encoding.UTF8.GetBytes("test data for signing");
                var signature = new byte[SignatureSize];

                BcEd25519.Sign(unmanagedPrivateKey, data, signature);
                var result = BcEd25519.Verify(unmanagedPublicKey, data, signature);

                Assert.True(result);
            }
        }

        [Fact]
        public void CanSignAndVerifyByExtendedKey()
        {
            for (var i = 64; i < 100; ++i)
            {
                var secret = Rand.SecureRandomBytes(i);

                // prune secret
                secret[0] &= 0b11111000;  // the lowest 3 bits kL are cleared (&= 0xF8)
                secret[31] &= 0b01111111; // the highest bit kL is cleared (&= 7F)
                secret[31] |= 0b01000000; // the second highest bit kL is set (|= 0x40)

                BcEd25519.GeneratePublicKeyFromExtended(
                    new SecureBytes(secret),
                    out var publicKey);

                using var unmanagedPublicKey = publicKey.ToUnmanagedBytes();

                var data = Encoding.UTF8.GetBytes("test data for signing");
                var signature = new byte[SignatureSize];

                BcEd25519.SignWithExtendedKey(secret, data, signature);
                var result = BcEd25519.Verify(unmanagedPublicKey, data, signature);

                Assert.True(result);
            }
        }
    }
}