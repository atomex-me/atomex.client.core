using Org.BouncyCastle.Crypto;

namespace Atomex.Cryptography
{
    public interface IAsymmetricCipher
    {
        AsymmetricCipherKeyPair GenerateKeyPair();
    }
}