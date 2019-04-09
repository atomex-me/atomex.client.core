using Atomix.Blockchain.Tezos;
using Atomix.Blockchain.Tezos.Internal;
using Atomix.Common;
using Atomix.Cryptography;
using Atomix.Wallet.Abstract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Security;
using System.Threading.Tasks;

namespace Atomix.Wallet.KeyData
{
    public class TezosHdKeyData : IHdKeyData
    {
        private const string TypeKey = "Type";

        public string EncryptedSeed { get; set; }
        public uint Account { get; set; }

        [JsonIgnore]
        public TezosWallet Wallet { get; set; }

        [JsonIgnore]
        public bool IsLocked => Wallet == null;

        public TezosHdKeyData(string mnemonic, SecureString passPhrase, uint account)
        {
            Account = account;

            Wallet = new TezosWallet(mnemonic, passPhrase.ToUnsecuredString());
        }

        public TezosHdKeyData(JObject keyData)
        {
            EncryptedSeed = keyData[nameof(EncryptedSeed)].Value<string>();
            Account = keyData[nameof(Account)].Value<uint>();
        }

        public void Encrypt(SecureString password)
        {
            try
            {
                var passwordHash = SessionPasswordHelper.GetSessionPasswordBytes(password);

                EncryptedSeed = Aes.Encrypt(
                        plainBytes: Wallet.Seed,
                        keyBytes: passwordHash)
                    .ToHexString();
            }
            catch (Exception e)
            {
                Log.Error(e, "Encrypt error");
            }
        }

        public Task EncryptAsync(SecureString password)
        {
            return Task.Factory.StartNew(() => Encrypt(password));
        }

        public void Lock()
        {
            Wallet = null;
        }

        public void Unlock(SecureString password)
        {
            try
            {
                var passwordHash = SessionPasswordHelper.GetSessionPasswordBytes(password);

                var seed = Aes.Decrypt(
                    encryptedBytes: Hex.FromString(EncryptedSeed),
                    keyBytes: passwordHash);

                Wallet = new TezosWallet(seed);
            }
            catch (Exception e)
            {
                Log.Error(e, "Unlock error");
            }
        }

        public byte[] GetPublicKey(KeyIndex keyIndex)
        {
            return GetPublicKey(keyIndex.Chain, keyIndex.Index);
        }

        public byte[] GetPublicKey(uint chain, uint index)
        {
            var publicKeyEncoded = Wallet.Keys.DecryptPublicKey();
            return Base58Check.Decode(publicKeyEncoded, Prefix.Edpk);
        }

        public byte[] GetPrivateKey(KeyIndex keyIndex)
        {
            return GetPrivateKey(keyIndex.Chain, keyIndex.Index);
        }

        public byte[] GetPrivateKey(uint chain, uint index)
        {
            var privateKeyEncoded = Wallet.Keys.DecryptPrivateKey();
            return Base58Check.Decode(privateKeyEncoded, Prefix.Edsk);
        }

        public byte[] SignHash(byte[] hash, KeyIndex keyIndex)
        {
            return SignHash(hash, keyIndex.Chain, keyIndex.Index);
        }

        public byte[] SignHash(byte[] hash, uint chain, uint index)
        {
            return new TezosSigner().Sign(
                data: hash,
                privateKey: Base58Check.Decode(Wallet.Keys.DecryptPrivateKey(), Prefix.Edsk));
        }

        public byte[] SignMessage(byte[] data, KeyIndex keyIndex)
        {
            return SignMessage(data, keyIndex.Chain, keyIndex.Index);
        }

        public byte[] SignMessage(byte[] data, uint chain, uint index)
        {
            return new TezosSigner().Sign(
                data: data,
                privateKey: Base58Check.Decode(Wallet.Keys.DecryptPrivateKey(), Prefix.Edsk));
        }

        public bool VerifyHash(byte[] hash, byte[] signature, KeyIndex keyIndex)
        {
            return VerifyHash(hash, signature, keyIndex.Chain, keyIndex.Index);
        }

        public bool VerifyHash(byte[] hash, byte[] signature, uint chain, uint index)
        {
            return new TezosSigner().Verify(
                data: hash,
                signature: signature,
                publicKey: Base58Check.Decode(Wallet.Keys.DecryptPublicKey(), Prefix.Edpk));
        }

        public bool VerifyMessage(byte[] data, byte[] signature, KeyIndex keyIndex)
        {
            return VerifyMessage(data, signature, keyIndex.Chain, keyIndex.Index);
        }

        public bool VerifyMessage(byte[] data, byte[] signature, uint chain, uint index)
        {
            return new TezosSigner().Verify(
                data: data,
                signature: signature,
                publicKey: Base58Check.Decode(Wallet.Keys.DecryptPublicKey(), Prefix.Edpk));
        }

        public JObject ToJsonObject()
        {
            return new JObject
            {
                [TypeKey] = nameof(TezosHdKeyData),
                [nameof(EncryptedSeed)] = EncryptedSeed,
                [nameof(Account)] = Account
            };
        }
    }
}