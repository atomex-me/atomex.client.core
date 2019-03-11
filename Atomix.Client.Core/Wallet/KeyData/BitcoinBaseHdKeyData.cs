using System;
using System.Security;
using System.Threading.Tasks;
using Atomix.Common;
using Atomix.Wallet.Abstract;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Atomix.Wallet.KeyData
{
    public class BitcoinBaseHdKeyData : IHdKeyData
    {
        public const string TypeKey = "Type";

        public string EncryptedKeyWif { get; set; }
        public string PublicKeyWif { get; set; }

        [JsonIgnore]
        public ExtKey Key { get; set; }

        [JsonIgnore]
        public bool IsLocked => Key == null;

        public BitcoinBaseHdKeyData(
            string mnemonic,
            Wordlist wordList,
            SecureString passPhrase,
            uint purpose,
            uint currency,
            uint account = 0)
        {
            Key = new Mnemonic(mnemonic, wordList)
                .DeriveExtKey(passPhrase.ToUnsecuredString())
                .Derive(new KeyPath(path: $"{purpose}'/{currency}'/{account}'"));

            PublicKeyWif = Key.Neuter().GetWif(Network.Main).ToWif();
        }

        public BitcoinBaseHdKeyData(JObject keyData)
        {
            EncryptedKeyWif = keyData[nameof(EncryptedKeyWif)].Value<string>();
            PublicKeyWif = keyData[nameof(PublicKeyWif)].Value<string>();
        }

        public void Encrypt(SecureString password)
        {
            EncryptedKeyWif = Key.PrivateKey
                .GetEncryptedBitcoinSecret(password.ToUnsecuredString(), Network.Main)
                .ToWif();
        }

        public Task EncryptAsync(SecureString password)
        {
            return Task.Factory.StartNew(() => Encrypt(password));
        }

        public void Lock()
        {
            Key = null;
        }

        public void Unlock(SecureString password)
        {
            var extPublicKey = ExtPubKey.Parse(PublicKeyWif, Network.Main);

            Key = new ExtKey(
                extPubKey: extPublicKey,
                privateKey: new BitcoinEncryptedSecretNoEC(EncryptedKeyWif)
                    .GetKey(password.ToUnsecuredString())
            );
        }

        public byte[] GetPublicKey(uint chain, uint index)
        {
            var extPublicKey = ExtPubKey.Parse(PublicKeyWif, Network.Main);

            return extPublicKey
                .Derive(chain)
                .Derive(index)
                .PubKey
                .ToBytes();
        }

        public byte[] GetPrivateKey(KeyIndex keyIndex)
        {
            return GetPrivateKey(keyIndex.Chain, keyIndex.Index);
        }

        public byte[] GetPrivateKey(uint chain, uint index)
        {
            return Key
                .Derive(chain)
                .Derive(index)
                .PrivateKey
                .ToBytes();
        }

        public byte[] SignHash(byte[] hash, KeyIndex keyIndex)
        {
            return SignHash(hash, keyIndex.Chain, keyIndex.Index);
        }

        public byte[] SignHash(byte[] hash, uint chain, uint index)
        {
            var key = Key.Derive(chain)
                .Derive(index)
                .PrivateKey;

            var transactionSignature = key.Sign(new uint256(hash), SigHash.All);

            return transactionSignature.ToBytes();
        }

        public byte[] SignMessage(byte[] data, KeyIndex keyIndex)
        {
            return SignMessage(data, keyIndex.Chain, keyIndex.Index);
        }

        public byte[] SignMessage(byte[] data, uint chain, uint index)
        {
            var key = Key
                .Derive(chain)
                .Derive(index);

            var signature = key.PrivateKey.SignMessage(data);

            return Convert.FromBase64String(signature);
        }

        public bool VerifyHash(byte[] hash, byte[] signature, KeyIndex keyIndex)
        {
            return VerifyHash(hash, signature, keyIndex.Chain, keyIndex.Index);
        }

        public bool VerifyHash(byte[] hash, byte[] signature, uint chain, uint index)
        {
            if (hash == null)
                throw new ArgumentNullException(nameof(hash));

            if (signature == null)
                throw new ArgumentNullException(nameof(signature));

            var pubKey = Key
                .Derive(chain)
                .Derive(index)
                .PrivateKey
                .PubKey;

            return pubKey.Verify(new uint256(hash), new TransactionSignature(signature).Signature);
        }

        public bool VerifyMessage(byte[] data, byte[] signature, KeyIndex keyIndex)
        {
            return VerifyMessage(data, signature, keyIndex.Chain, keyIndex.Index);
        }

        public bool VerifyMessage(byte[] data, byte[] signature, uint chain, uint index)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (signature == null)
                throw new ArgumentNullException(nameof(signature));

            var pubKey = Key
                .Derive(chain)
                .Derive(index)
                .PrivateKey
                .PubKey;

            return pubKey.VerifyMessage(data, Convert.ToBase64String(signature));
        }

        public JObject ToJsonObject()
        {
            return new JObject
            {
                [TypeKey] = nameof(BitcoinBaseHdKeyData),
                [nameof(EncryptedKeyWif)] = EncryptedKeyWif,
                [nameof(PublicKeyWif)] = PublicKeyWif
            };
        }
    }
}