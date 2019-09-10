namespace Atomix.Cryptography
{
    public interface IKey
    {
        void GetPrivateKey(out byte[] privateKey);
        void GetPublicKey(out byte[] publicKey);
        byte[] SignHash(byte[] hash);
        byte[] SignMessage(byte[] data);
        bool VerifyHash(byte[] hash, byte[] signature);
        bool VerifyMessage(byte[] data, byte[] signature);
    }
}