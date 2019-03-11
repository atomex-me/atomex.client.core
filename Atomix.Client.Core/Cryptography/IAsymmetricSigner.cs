using Org.BouncyCastle.Crypto;

namespace Atomix.Cryptography
{
    public interface IAsymmetricSigner : ISigner
    {
        byte[] Sign(AsymmetricKeyParameter privateKey, byte[] data);
    }
}