using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Network = Atomex.Core.Network;
using Atomex.Blockchain.BitcoinBased;

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
            PathToWallet = FileSystem.Current.ToFullPath(pathToWallet);

            KeyStorage = HdKeyStorageLoader.LoadFromFile(pathToWallet, password)
                .Unlock(password);
        }

        public HdWallet(
            string mnemonic,
            Wordlist wordList,
            SecureString passPhrase = null,
            Network network = Network.MainNet)
        {
            PathToWallet = FileSystem.Current.ToFullPath(string.Empty);

            KeyStorage = new HdKeyStorage(
                mnemonic: mnemonic,
                wordList: wordList,
                passPhrase: passPhrase,
                network: network);
        }

        public void Lock() =>
            KeyStorage.Lock();

        public void Unlock(SecureString password) =>
            KeyStorage.Unlock(password);

        public Task EncryptAsync(SecureString password) =>
            KeyStorage.EncryptAsync(password);

        public WalletAddress GetAddress(
            CurrencyConfig currency,
            KeyIndex keyIndex,
            int keyType)
        {
            return GetAddress(
                currency: currency,
                account: keyIndex.Account,
                chain: keyIndex.Chain,
                index: keyIndex.Index,
                keyType: keyType);
        }

        public WalletAddress GetAddress(
            CurrencyConfig currency,
            uint account,
            uint chain,
            uint index,
            int keyType)
        {
            using var securePublicKey = KeyStorage.GetPublicKey(
                currency: currency,
                account: account,
                chain: chain,
                index: index,
                keyType: keyType);

            if (securePublicKey == null)
                return null;

            var publicKey = securePublicKey.ToUnsecuredBytes();

            var address = currency.AddressFromKey(publicKey);

            return new WalletAddress
            {
                Currency  = currency.Name,
                Address   = address,
                KeyIndex  = new KeyIndex { Account = account, Chain = chain, Index = index },
                KeyType   = keyType
            };
        }

        public SecureBytes GetPublicKey(
            CurrencyConfig currency,
            KeyIndex keyIndex,
            int keyType) =>
            KeyStorage.GetPublicKey(currency, keyIndex, keyType);

        public SecureBytes GetServicePublicKey(uint index) =>
            KeyStorage.GetServicePublicKey(index);

        public Task<byte[]> SignAsync(
            byte[] data,
            WalletAddress address,
            CurrencyConfig currency,
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

            var signature = KeyStorage.SignHash(
                currency: currency,
                hash: data,
                keyIndex: address.KeyIndex,
                keyType: address.KeyType);

            Log.Verbose("Data signature in base64: {@signature}",
                Convert.ToBase64String(signature));

            if (!KeyStorage.VerifyHash(
                currency: currency,
                hash: data,
                signature: signature,
                keyIndex: address.KeyIndex,
                keyType: address.KeyType))
            {
                Log.Error("Signature verify error");
                return Task.FromResult<byte[]>(null);
            }

            Log.Verbose("Data successfully signed");

            return Task.FromResult(signature);      
        }

        public async Task<bool> SignAsync(
            BitcoinBasedTransaction tx,
            IEnumerable<BitcoinBasedTxOutput> spentOutputs,
            IAddressResolver addressResolver,
            CurrencyConfig currencyConfig,
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
                    currencyConfig: currencyConfig,
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
            CurrencyConfig currencyConfig,
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
                .SignAsync(KeyStorage, address, currencyConfig, cancellationToken)
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
            CurrencyConfig currencyConfig,
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

            var signature = KeyStorage.SignHash(
                currency: currencyConfig,
                hash: hash,
                keyIndex: address.KeyIndex,
                keyType: address.KeyType);

            Log.Verbose("Hash signature in base64: {@signature}", Convert.ToBase64String(signature));

            if (!KeyStorage.VerifyHash(
                currency: currencyConfig,
                hash: hash,
                signature: signature,
                keyIndex: address.KeyIndex,
                keyType: address.KeyType))
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

            var signature = KeyStorage.SignByServiceKey(
                data: data,
                chain: 0,
                index: keyIndex);

            Log.Verbose("Signature in base64: {@signature}", Convert.ToBase64String(signature));

            if (!KeyStorage.VerifyByServiceKey(data, signature, chain: 0, index: keyIndex))
            {
                Log.Error("Signature verify error");
                return null;
            }

            Log.Verbose("Data successfully signed by service key");

            return Task.FromResult(signature);
        }

        public byte[] GetDeterministicSecret(CurrencyConfig currency, DateTime timeStamp) =>
            KeyStorage.GetDeterministicSecret(currency, timeStamp);

        public static HdWallet LoadFromFile(string pathToWallet, SecureString password) =>
            new(pathToWallet, password);

        public bool SaveToFile(string pathToWallet, SecureString password)
        {
            var walletDirectory = Path.GetDirectoryName(pathToWallet);

            if (walletDirectory == null)
                throw new InvalidOperationException();

            if (!Directory.Exists(walletDirectory))
                Directory.CreateDirectory(walletDirectory);

            return KeyStorage.SaveToFile(pathToWallet, password);
        }
    }
}