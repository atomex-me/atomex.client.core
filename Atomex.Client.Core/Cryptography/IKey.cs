using System;

using Atomex.Common.Memory;

namespace Atomex.Cryptography
{
    public interface IKey : IDisposable
    {
        SecureBytes GetPrivateKey();
        SecureBytes GetPublicKey();
        byte[] SignHash(byte[] hash);
        byte[] SignMessage(byte[] data);
        bool VerifyHash(byte[] hash, byte[] signature);
        bool VerifyMessage(byte[] data, byte[] signature);
    }
}