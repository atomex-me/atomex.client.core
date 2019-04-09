using System;
using System.Security;
using System.Threading.Tasks;
using Atomix.Common;
using Atomix.Cryptography;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.Bip;
using Nethereum.Signer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomix.Wallet.KeyData
{
    public class EthereumHdKeyData : IHdKeyData
    {
        public const string TypeKey = "Type";

        public string EncryptedExternalChainSeed { get; set; }
        public string EncryptedInternalChainSeed { get; set; }
        public uint Account { get; set; }

        [JsonIgnore]
        public Nethereum.HdWallet.Wallet ExternalWallet { get; set; }

        [JsonIgnore]
        public Nethereum.HdWallet.Wallet InternalWallet { get; set; }

        [JsonIgnore]
        public bool IsLocked => ExternalWallet == null && InternalWallet == null;

        [JsonIgnore]
        public string ExternalKeyPath => $"m/{Bip44.Purpose}'/{Bip44.Ethereum}'/{Account}'/{Bip44.External}/x";

        [JsonIgnore]
        public string InternalKeyPath => $"m/{Bip44.Purpose}'/{Bip44.Ethereum}'/{Account}'/{Bip44.Internal}/x";

        public EthereumHdKeyData(string mnemonic, SecureString passPhrase, uint account)
        {
            Account = account;

            ExternalWallet = new Nethereum.HdWallet.Wallet(
                words: mnemonic,
                seedPassword: passPhrase.ToUnsecuredString(),
                path: ExternalKeyPath);

            InternalWallet = new Nethereum.HdWallet.Wallet(
                words: mnemonic,
                seedPassword: passPhrase.ToUnsecuredString(),
                path: InternalKeyPath);
        }

        public EthereumHdKeyData(JObject keyData)
        {
            EncryptedExternalChainSeed = keyData[nameof(EncryptedExternalChainSeed)].Value<string>();
            EncryptedInternalChainSeed = keyData[nameof(EncryptedInternalChainSeed)].Value<string>();
            Account = keyData[nameof(Account)].Value<uint>();
        }

        public void Encrypt(SecureString password)
        {
            try
            {
                var passwordHash = SessionPasswordHelper.GetSessionPasswordBytes(password);

                EncryptedExternalChainSeed = Aes.Encrypt(
                        plainBytes: Hex.FromString(ExternalWallet.Seed),
                        keyBytes: passwordHash)
                    .ToHexString();

                EncryptedInternalChainSeed = Aes.Encrypt(
                        plainBytes: Hex.FromString(InternalWallet.Seed),
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
            ExternalWallet = null;
            InternalWallet = null;
        }

        public void Unlock(SecureString password)
        {
            try
            {
                var passwordHash = SessionPasswordHelper.GetSessionPasswordBytes(password);

                ExternalWallet = new Nethereum.HdWallet.Wallet(
                    seed: Aes.Decrypt(
                        encryptedBytes: Hex.FromString(EncryptedExternalChainSeed),
                        keyBytes: passwordHash),
                    path: ExternalKeyPath);

                InternalWallet = new Nethereum.HdWallet.Wallet(
                    seed: Aes.Decrypt(
                        encryptedBytes: Hex.FromString(EncryptedInternalChainSeed),
                        keyBytes: passwordHash),
                    path: InternalKeyPath);
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
            return GetEcKey(chain, index)
                .GetPubKey();
        }

        public byte[] GetPrivateKey(KeyIndex keyIndex)
        {
            return GetPrivateKey(keyIndex.Chain, keyIndex.Index);
        }

        public byte[] GetPrivateKey(uint chain, uint index)
        {
            var wallet = GetWalletByChain(chain);

            if (wallet == null)
                throw new Exception("Not supported chain");

            return wallet.GetPrivateKey((int)index);
        }

        public byte[] SignHash(byte[] hash, KeyIndex keyIndex)
        {
            return SignHash(hash, keyIndex.Chain, keyIndex.Index);
        }

        public byte[] SignHash(byte[] hash, uint chain, uint index)
        {
            return GetEcKey(chain, index)
                .Sign(hash)
                .ToDER();
        }

        public byte[] SignMessage(byte[] data, KeyIndex keyIndex)
        {
            return SignMessage(data, keyIndex.Chain, keyIndex.Index);
        }

        public byte[] SignMessage(byte[] data, uint chain, uint index)
        {
            //var signer = new EthereumMessageSigner();
            //return Hex.FromString(signer.Sign(data, GetEcKey(chain, index)));

            // TODO: check Nethereum.Signer for the ability to sign arbitrary data
            return GetEcKey(chain, index)
                .Sign(data)
                .ToDER();
        }

        public bool VerifyHash(byte[] hash, byte[] signature, KeyIndex keyIndex)
        {
            return VerifyHash(hash, signature, keyIndex.Chain, keyIndex.Index);
        }

        public bool VerifyHash(byte[] hash, byte[] signature, uint chain, uint index)
        {
            //var signer = new EthereumMessageSigner();
            //signer.

            return GetEcKey(chain, index)
                .Verify(hash, EthECDSASignature.FromDER(signature));
        }

        public bool VerifyMessage(byte[] data, byte[] signature, KeyIndex keyIndex)
        {
            return VerifyMessage(data, signature, keyIndex.Chain, keyIndex.Index);
        }

        public bool VerifyMessage(byte[] data, byte[] signature, uint chain, uint index)
        {
            // TODO: check Nethereum.Signer for the ability to verify arbitrary data
            return GetEcKey(chain, index)
                .Verify(data, EthECDSASignature.FromDER(signature));
        }

        public JObject ToJsonObject()
        {
            return new JObject
            {
                [TypeKey] = nameof(EthereumHdKeyData),
                [nameof(EncryptedExternalChainSeed)] = EncryptedExternalChainSeed,
                [nameof(EncryptedInternalChainSeed)] = EncryptedInternalChainSeed,
                [nameof(Account)] = Account
            };
        }

        private Nethereum.HdWallet.Wallet GetWalletByChain(uint chain)
        {
            return chain == Bip44.External
                ? ExternalWallet
                : chain == Bip44.Internal
                    ? InternalWallet
                    : null;
        }

        private EthECKey GetEcKey(uint chain, uint index)
        {
            return new EthECKey(GetPrivateKey(chain, index).ToHexString());
        }
    }
}