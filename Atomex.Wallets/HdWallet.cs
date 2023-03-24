using System;
using System.Collections.Generic;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;

using Atomex.Common.Memory;
using Atomex.Wallets.Bips;

namespace Atomex.Wallets
{
    /// <summary>
    /// Represents an IWallet interface implementation for regular HD-wallets (non hardwallets)
    /// </summary>
    /// <typeparam name="TExtKey">Extended key type</typeparam>
    public class HdWallet<TExtKey> : Wallet<TExtKey> where TExtKey : IExtKey
    {
        public HdWallet(
            SecureBytes seed,
            Func<SecureBytes, TExtKey> keyCreator = null,
            SignDataType signDataType = SignDataType.Plain)
            : base(seed, keyCreator, signDataType)
        {
        }

        public HdWallet(
            Func<SecureBytes> seedCreator, 
            Func<SecureBytes, TExtKey> keyCreator = null,
            SignDataType signDataType = SignDataType.Plain)
            : base(seedCreator, keyCreator, signDataType)
        {
        }

        /// <summary>
        /// Create HD wallet from <paramref name="mnemonic"/> with words from <paramref name="wordList"/> and optional <paramref name="passPhrase"/>. Also custom keyCreator function can be used.
        /// </summary>
        /// <param name="mnemonic">BIP39 mnemonic</param>
        /// <param name="wordList">Mnemonic word list</param>
        /// <param name="passPhrase">Passphrase for additional security (optional)</param>
        /// <param name="keyCreator">Custom key creation function (optional)</param>
        public HdWallet(
            SecureString mnemonic,
            Wordlist wordList,
            SecureString passPhrase = null,
            Func<SecureBytes, TExtKey> keyCreator = null,
            SignDataType signDataType = SignDataType.Plain)
        {
            _seed = Bip39.SeedFromMnemonic(mnemonic, wordList, passPhrase);
            _keyCreator = keyCreator ?? new Func<SecureBytes, TExtKey>((s) => (TExtKey)DefaultConstructor(s));
            SignDataType = signDataType;
        }

        public override Task<byte[]> GetPublicKeyAsync(
            string keyPath,
            CancellationToken cancellationToken = default)
        {
            using var key = CreateKey(keyPath);

            return Task.FromResult(key.GetPublicKey());
        }

        public override async Task<byte[]> SignAsync(
            ReadOnlyMemory<byte> data,
            string keyPath,
            CancellationToken cancellationToken = default)
        {
            using var key = CreateKey(keyPath);

            return await key
                .SignAsync(data, cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<IList<byte[]>> SignAsync(
            IList<ReadOnlyMemory<byte>> data,
            IList<string> keyPathes,
            CancellationToken cancellationToken = default)
        {
            if (data.Count != keyPathes.Count)
                return null;

            var signatures = new List<byte[]>(data.Count);

            for (var i = 0; i < data.Count; ++i)
            {
                var signature = await SignAsync(
                        data: data[i],
                        keyPath: keyPathes[i],
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                signatures.Add(signature);
            }

            return signatures;
        }

        public override async Task<bool> VerifyAsync(
            ReadOnlyMemory<byte> data,
            ReadOnlyMemory<byte> signature,
            string keyPath,
            CancellationToken cancellationToken = default)
        {
            using var key = CreateKey(keyPath);

            return await key
                .VerifyAsync(data, signature, cancellationToken)
                .ConfigureAwait(false);
        }

        protected override IKey CreateKey(string keyPath)
        {
            using var seed = _seed != null
                ? _seed.Copy()
                : _seedCreator();

            using var masterKey = _keyCreator(_seed);

            return masterKey.Derive(keyPath);
        }
    }
}