namespace Atomix.Cryptography
{
    public interface ISigner
    {
        bool VerifySign(byte[] sign, byte[] data, byte[] publicKey);
        byte[] Sign(byte[] data, byte[] privateKey);
    }
}