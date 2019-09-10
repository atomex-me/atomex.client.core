using System.Security;
using System.Text;
using Atomex.Cryptography;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class AesTests
    {
        private static SecureString CreatePassword()
        {
            var pwd = new SecureString();

            pwd.AppendChar('t');
            pwd.AppendChar('e');
            pwd.AppendChar('s');
            pwd.AppendChar('t');
            pwd.AppendChar('p');
            pwd.AppendChar('a');
            pwd.AppendChar('s');
            pwd.AppendChar('s');

            return pwd;
        }

        [Fact]
        public void EncryptDecryptTest()
        {
            var password = CreatePassword();
            var plainBytes = Encoding.ASCII.GetBytes("testdata");

            var encryptedBytes = Aes.Encrypt(plainBytes, password);
            var decryptedBytes = Aes.Decrypt(encryptedBytes, password);

            Assert.Equal(plainBytes, decryptedBytes);
        }
    }
}