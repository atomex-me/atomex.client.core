using System.Security;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Atomix.Wallet.Abstract
{
    public interface IHdKeyData
    {
        bool IsLocked { get; }

        void Encrypt(SecureString password);
        Task EncryptAsync(SecureString password);
        void Lock();
        void Unlock(SecureString password);
        byte[] GetPublicKey(KeyIndex keyIndex);
        byte[] GetPublicKey(uint chain, uint index);
        byte[] GetPrivateKey(KeyIndex keyIndex);
        byte[] GetPrivateKey(uint chain, uint index);
        byte[] SignHash(byte[] hash, KeyIndex keyIndex);
        byte[] SignHash(byte[] hash, uint chain, uint index);
        byte[] SignMessage(byte[] data, KeyIndex keyIndex);
        byte[] SignMessage(byte[] data, uint chain, uint index);
        bool VerifyHash(byte[] hash, byte[] signature, KeyIndex keyIndex);
        bool VerifyHash(byte[] hash, byte[] signature, uint chain, uint index);
        bool VerifyMessage(byte[] data, byte[] signature, KeyIndex keyIndex);
        bool VerifyMessage(byte[] data, byte[] signature, uint chain, uint index);
        JObject ToJsonObject();
    }
}