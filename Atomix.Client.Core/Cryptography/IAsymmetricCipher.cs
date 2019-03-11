using Org.BouncyCastle.Crypto;

namespace Atomix.Cryptography
{
    public interface IAsymmetricCipher
    {
        AsymmetricCipherKeyPair GenerateKeyPair();
    }
}