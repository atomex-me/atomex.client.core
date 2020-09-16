using System.Text;
using Atomex.Common;
using Atomex.Cryptography;
using Xunit;

namespace Atomex.Client.Core.Tests.Cryptography
{
    public class AesTests
    {
        [Fact]
        public void EncryptDecryptTest()
        {
            var password = "testpass".ToSecureString();
            var plainBytes = Encoding.ASCII.GetBytes("testdata");

            var encryptedBytes = Aes.Encrypt(plainBytes, password);
            var decryptedBytes = Aes.Decrypt(encryptedBytes, password);

            Assert.Equal(plainBytes, decryptedBytes);
        }
    }
}