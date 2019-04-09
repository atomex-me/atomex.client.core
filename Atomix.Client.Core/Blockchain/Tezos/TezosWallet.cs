using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomix.Blockchain.Tezos.Internal;
using Atomix.Blockchain.Tezos.Internal.OperationResults;
using Atomix.Cryptography;
using NBitcoin;

namespace Atomix.Blockchain.Tezos
{
    public class TezosWallet
    {
        /// <summary>
        /// Create wallet from mnemonic and passphrase with deterministic seed.
        /// </summary>
        /// <param name="mnemonic">Mnemonic</param>
        /// <param name="passPhrase">Passphrase to generate seed</param>
        public TezosWallet(string mnemonic, string passPhrase)
        {
            FromMnemonic(mnemonic, passPhrase);
        }

        public TezosWallet(IEnumerable<string> words, string passPhrase)
        {
            FromMnemonic(string.Join(" ", words), passPhrase);
        }

        /// <summary>
        /// Generate wallet from seed.
        /// </summary>
        /// <param name="seed">Seed to generate keys with</param>
        public TezosWallet(byte[] seed)
        {
            FromSeed(seed);
        }

        private void FromMnemonic(string mnemonic, string passPhrase)
        {
            FromSeed(new Mnemonic(mnemonic, Wordlist.English).DeriveSeed(passPhrase));
        }

        private void FromSeed(byte[] seed)
        {
            if (seed?.Any() == false)
                throw new ArgumentException("Seed required", nameof(seed));

            Seed = seed;

            byte[] publicKey = null, privateKey = null;

            try
            {
                new Ed25519().GenerateKeyPair(Seed, out privateKey, out publicKey);

                // Also creates the tz1 PK hash.
                Keys = new Keys(publicKey, privateKey);
            }
            finally
            {
                if (privateKey != null)
                    Array.Clear(privateKey, 0, privateKey.Length);

                if (publicKey != null)
                    Array.Clear(publicKey, 0, publicKey.Length);
            }
        }

        /// <summary>
        /// Activate this wallet on the blockchain. This can only be done once.
        /// </summary>
        /// <param name="activationCode">The blinded publich hash used to activate this wallet.</param>
        /// <returns>The result of the activation operation.</returns>
        public async Task<OperationResult> Activate(string activationCode)
        {
            return await new Rpc(Provider)
                .Activate(Keys.PublicHash, activationCode)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Get the balance of the wallet.
        /// </summary>
        /// <returns>The balance of the wallet.</returns>
        public async Task<decimal> GetBalance()
        {
            return await new Rpc(Provider)
                .GetBalance(Keys.PublicHash)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Transfer funds from one wallet to another.
        /// </summary>
        /// <param name="from">From where to transfer the funds.</param>
        /// <param name="to">To where the funds should be transferred.</param>
        /// <param name="amount">The amount to transfer.</param>
        /// <param name="fee">The fee to transfer.</param>
        /// <param name="gasLimit">The gas limit to transfer.</param>
        /// <param name="storageLimit">The storage limit to transfer.</param>
        /// <returns>The result of the transfer operation.</returns>
        public async Task<OperationResult> Transfer(
            string from,
            string to,
            decimal amount,
            decimal fee,
            decimal gasLimit,
            decimal storageLimit)
        {
            return await new Rpc(Provider)
                .SendTransaction(Keys, from, to, amount, fee, gasLimit, storageLimit)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// The seed used to generate the wallet keys.
        /// </summary>
        public byte[] Seed { get; internal set; }

        /// <summary>
        /// The encrypted public/private keys.
        /// </summary>
        public Keys Keys { get; internal set; }

        /// <summary>
        /// This wallet's public hash.
        /// </summary>
        public string PublichHash => Keys?.PublicHash;

        /// <summary>
        /// Get or set the provider address to make RPC calls to.
        /// </summary>
        public string Provider { get; set; } = Currencies.Xtz.RpcProvider;

        public static TezosWallet FromStringSeed(string seed, byte[] prefix = null)
        {
            return new TezosWallet(Base58Check.Decode(seed, prefix ?? Prefix.Edsk));
        }
    }
}