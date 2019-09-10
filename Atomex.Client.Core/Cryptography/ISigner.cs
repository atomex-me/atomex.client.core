namespace Atomex.Cryptography
{
    public interface ISigner
    {
        bool VerifySign(byte[] data, byte[] sign, byte[] publicKey);
        byte[] Sign(byte[] data, byte[] privateKey);
    }
}