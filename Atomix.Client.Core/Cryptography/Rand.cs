using System.Security.Cryptography;

namespace Atomix.Cryptography
{
    public class Rand
    {
        public static byte[] SecureRandomBytes(int length)
        {
            var result = new byte[length];

            using (var provider = new RNGCryptoServiceProvider())
            {
                provider.GetBytes(result);

                return result;
            }
        }
    }
}