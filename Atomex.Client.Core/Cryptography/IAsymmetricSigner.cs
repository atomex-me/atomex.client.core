using Org.BouncyCastle.Crypto;

namespace Atomex.Cryptography
{
    public interface IAsymmetricSigner : ISigner
    {
        byte[] Sign(AsymmetricKeyParameter privateKey, byte[] data);
    }
}