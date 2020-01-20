using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using NBitcoin;
using Serilog;
using Network = Atomex.Core.Network;

namespace Atomex.Wallet
{
    public class HdWallet : IHdWallet
    {
        public HdKeyStorage KeyStorage { get; }

        public string PathToWallet { get; set; }
        public Network Network => KeyStorage.Network;
        public bool IsLocked => KeyStorage.IsLocked;

        private HdWallet(string pathToWallet, SecureString password)
        {
            PathToWallet = PathEx.ToFullPath(pathToWallet);

            KeyStorage = HdKeyStorageLoader.LoadFromFile(pathToWallet, password)
                .Unlock(password);
        }

        public HdWallet(
            string mnemonic,
            Wordlist wordList,
            SecureString passPhrase = null,
            Network network = Network.MainNet)
        {
            PathToWallet = PathEx.ToFullPath(string.Empty);

            KeyStorage = new HdKeyStorage(
                mnemonic: mnemonic,
                wordList: wordList,
                passPhrase: passPhrase,
                network: network);
        }

        public HdWallet(Network network = Network.MainNet)
            : this(mnemonic: new Mnemonic(Wordlist.English, WordCount.Fifteen).ToString(),
                   wordList: Wordlist.English,
                   passPhrase: null,
                   network: network)
        {
        }

        public void Lock()
        {
            KeyStorage.Lock();
        }

        public void Unlock(SecureString password)
        {
            KeyStorage.Unlock(password);
        }

        public Task EncryptAsync(SecureString password)
        {
            return KeyStorage.EncryptAsync(password);
        }

        public WalletAddress GetAddress(Currency currency, int chain, uint index)
        {
            using var securePublicKey = KeyStorage.GetPublicKey(currency, chain, index);

            if (securePublicKey == null)
                return null;

            using var publicKey = securePublicKey.ToUnsecuredBytes();

            var address = currency.AddressFromKey(publicKey);

            return new WalletAddress
            {
                Currency = currency,
                Address = address,
                PublicKey = Convert.ToBase64String(publicKey),
                KeyIndex = new KeyIndex { Chain = chain, Index = index }
            };
        }

        public SecureBytes GetServicePublicKey(uint index)
        {
            return KeyStorage.GetServicePublicKey(index);
        }

        public Task<byte[]> SignAsync(
            byte[] data,
            WalletAddress address,
            CancellationToken cancellationToken = default)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (address == null)
                throw new ArgumentNullException(nameof(address));

            Log.Verbose("Sign request for data {@data} with key for address {@address}", 
                data.ToHexString(),
                address.Address);

            if (IsLocked)
            {
                Log.Warning("Wallet locked");
                return Task.FromResult<byte[]>(null);
            }

            if (address.KeyIndex == null)
            {
                Log.Error($"Can't find private key for address {address.Address}");
                return Task.FromResult<byte[]>(null);
            }

            var signature = KeyStorage.SignMessage(address.Currency, data, address.KeyIndex);

            Log.Verbose("Data signature in base64: {@signature}",
                Convert.ToBase64String(signature));

            if (!KeyStorage.VerifyMessage(address.Currency, data, signature, address.KeyIndex))
            {
                Log.Error("Signature verify error");
                return Task.FromResult<byte[]>(null);
            }

            Log.Verbose("Data successfully signed");

            return Task.FromResult(signature);      
        }

        public async Task<bool> SignAsync(
            IInOutTransaction tx,
            IEnumerable<ITxOutput> spentOutputs,
            IAddressResolver addressResolver,
            CancellationToken cancellationToken = default)
        {
            if (tx == null)
                throw new ArgumentNullException(nameof(tx));

            Log.Verbose("Sign request for transaction {@id}", tx.Id);

            if (IsLocked)
            {
                Log.Warning("Wallet locked");
                return false;
            }

            var signResult = await tx
                .SignAsync(
                    addressResolver: addressResolver,
                    keyStorage: KeyStorage,
                    spentOutputs: spentOutputs,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (signResult)
                Log.Verbose("Transaction {@id} successfully signed", tx.Id);
            else
                Log.Error("Transaction {@id} signing error", tx.Id);

            return signResult;
        }

        public async Task<bool> SignAsync(
            IAddressBasedTransaction tx,
            WalletAddress address,
            CancellationToken cancellationToken = default)
        {
            if (tx == null)
                throw new ArgumentNullException(nameof(tx));

            Log.Verbose("Sign request for transaction {@id}", tx.Id);

            if (IsLocked)
            {
                Log.Warning("Wallet locked");
                return false;
            }

            var signResult = await tx
                .SignAsync(KeyStorage, address, cancellationToken)
                .ConfigureAwait(false);

            if (signResult)
                Log.Verbose("Transaction {@id} successfully signed", tx.Id);
            else
                Log.Error("Transaction {@id} signing error", tx.Id);

            return signResult;
        }

        public Task<byte[]> SignHashAsync(
            byte[] hash,
            WalletAddress address,
            CancellationToken cancellationToken = default)
        {
            if (hash == null)
                throw new ArgumentNullException(nameof(hash));

            if (address == null)
                throw new ArgumentNullException(nameof(address));

            Log.Verbose("Sign request for hash {@hash}", hash.ToHexString());

            if (IsLocked)
            {
                Log.Warning("Wallet locked");
                return Task.FromResult<byte[]>(null);
            }

            if (address.KeyIndex == null)
            {
                Log.Error($"Can't find private key for address {address.Address}");
                return Task.FromResult<byte[]>(null);
            }

            var signature = KeyStorage.SignHash(address.Currency, hash, address.KeyIndex);

            Log.Verbose("Hash signature in base64: {@signature}", Convert.ToBase64String(signature));

            if (!KeyStorage.VerifyHash(address.Currency, hash, signature, address.KeyIndex))
            {
                Log.Error("Signature verify error");
                return Task.FromResult<byte[]>(null);
            }

            Log.Verbose("Hash successfully signed");

            return Task.FromResult(signature);
        }

        public Task<byte[]> SignByServiceKeyAsync(
            byte[] data,
            uint keyIndex,
            CancellationToken cancellationToken = default)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            Log.Verbose("Service sign request for data {@data} with key index {@index}",
                data.ToHexString(),
                keyIndex);

            if (IsLocked)
            {
                Log.Warning("Wallet locked");
                return null;
            }

            var signature = KeyStorage.SignMessageByServiceKey(
                data: data,
                chain: 0,
                index: keyIndex);

            Log.Verbose("Signature in base64: {@signature}", Convert.ToBase64String(signature));

            if (!KeyStorage.VerifyMessageByServiceKey(data, signature, chain: 0, index: keyIndex))
            {
                Log.Error("Signature verify error");
                return null;
            }

            Log.Verbose("Data successfully signed by service key");

            return Task.FromResult(signature);
        }

        public byte[] GetDeterministicSecret(Currency currency, DateTime timeStamp)
        {
            return KeyStorage.GetDeterministicSecret(currency, timeStamp);
        }

        //public bool Verify(
        //    WalletAddress walletAddress,
        //    byte[] data,
        //    byte[] signature)
        //{
        //    if (walletAddress == null)
        //        throw new ArgumentNullException(nameof(walletAddress));

        //    if (data == null)
        //        throw new ArgumentNullException(nameof(data));

        //    if (signature == null)
        //        throw new ArgumentNullException(nameof(signature));

        //    return KeyStorage.VerifyMessage(
        //        currency: walletAddress.Currency,
        //        data: data,
        //        signature: signature,
        //        keyIndex: walletAddress.KeyIndex);
        //}

        public static HdWallet LoadFromFile(string pathToWallet, SecureString password)
        {
            return new HdWallet(pathToWallet, password);
        }

        public void SaveToFile(string pathToWallet, SecureString password)
        {
            var walletDirectory = Path.GetDirectoryName(pathToWallet);

            if (walletDirectory == null)
                throw new InvalidOperationException();

            if (!Directory.Exists(walletDirectory))
                Directory.CreateDirectory(walletDirectory);

            KeyStorage.SaveToFile(pathToWallet, password);
        }
    }
}