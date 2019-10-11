using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Core.Entities;
using Atomex.Cryptography;
using Atomex.Swaps.Abstract;
using Atomex.Wallet.Abstract;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.Blockchain.Tezos
{
    public class TezosTransaction : IAddressBasedTransaction
    {
        private const int DefaultConfirmations = 1;

        public string Id { get; set; }
        public Currency Currency { get; set; }
        public BlockInfo BlockInfo { get; set; }
        public BlockchainTransactionState State { get; set ; }
        public BlockchainTransactionType Type { get; set; }
        public DateTime? CreationTime { get; set; }
        public bool IsConfirmed => BlockInfo?.Confirmations >= DefaultConfirmations;

        public string From { get; set; }
        public string To { get; set; }
        public decimal Amount { get; set; }
        public decimal Fee { get; set; }
        public decimal GasLimit { get; set; }
        public decimal StorageLimit { get; set; }
        public decimal Burn { get; set; }
        public JObject Params { get; set; }
        public bool IsInternal { get; set; }
        public int InternalIndex { get; set; }

        public JArray Operations { get; private set; }
        public JObject Head { get; private set; }
        public SignedMessage SignedMessage { get; private set; }

        public List<TezosTransaction> InternalTxs { get; set; }

        public async Task<bool> SignAsync(
            IKeyStorage keyStorage,
            WalletAddress address,
            CancellationToken cancellationToken = default)
        {
            var xtz = (Atomex.Tezos) Currency;

            if (address.KeyIndex == null)
            {
                Log.Error("Can't find private key for address {@address}", address);
                return false;
            }

            var privateKey = keyStorage
                .GetPrivateKey(Currency, address.KeyIndex);

            if (privateKey == null)
            {
                Log.Error("Can't find private key for address {@address}", address);
                return false;
            }

            var publicKey = keyStorage
                .GetPublicKey(Currency, address.KeyIndex);

            var rpc = new Rpc(xtz.RpcNodeUri);

            Head = await rpc
                .GetHeader()
                .ConfigureAwait(false);

            var managerKey = await rpc
                .GetManagerKey(From)
                .ConfigureAwait(false);

            Operations = new JArray();

            var gas = GasLimit.ToString(CultureInfo.InvariantCulture);
            var storage = StorageLimit.ToString(CultureInfo.InvariantCulture);

            if (managerKey["key"] == null)
            {
                var revealOpCounter = await TezosCounter.Instance
                    .GetCounter(xtz, From, Head)
                    .ConfigureAwait(false);

                var revealOp = new JObject
                {
                    ["kind"] = OperationType.Reveal,
                    ["fee"] = "0",
                    ["public_key"] = Base58Check.Encode(publicKey, Prefix.Edpk),
                    ["source"] = From,
                    ["storage_limit"] = storage,
                    ["gas_limit"] = gas,
                    ["counter"] = revealOpCounter.ToString()
                };

                Operations.AddFirst(revealOp);
            }

            var counter = await TezosCounter.Instance
                .GetCounter(xtz, From, Head)
                .ConfigureAwait(false);

            var transaction = new JObject
            {
                ["kind"] = OperationType.Transaction,
                ["source"] = From,
                ["fee"] = Fee.ToString(CultureInfo.InvariantCulture),
                ["counter"] = counter.ToString(),
                ["gas_limit"] = gas,
                ["storage_limit"] = storage,
                ["amount"] = Math.Round(Amount, 0).ToString(CultureInfo.InvariantCulture),
                ["destination"] = To
            };

            Operations.Add(transaction);

            if (Params != null)
                transaction["parameters"] = Params;
            else
            {
                var parameters = new JObject
                {
                    ["prim"] = "Unit",
                    ["args"] = new JArray()
                };

                transaction["parameters"] = parameters;
            }

            var forgedOpGroup = await rpc
                .ForgeOperations(Head, Operations)
                .ConfigureAwait(false);

            SignedMessage = TezosSigner.SignHash(
                data: Hex.FromString(forgedOpGroup.ToString()),
                privateKey: privateKey,
                watermark: Watermark.Generic,
                isExtendedKey: privateKey.Length == 64);

            return true;
        }

        public bool IsSwapInit(long refundTimestamp, byte[] secretHash, string participant)
        {
            try
            {
                return Params["args"][0]["args"][0]["args"][1]["args"][0]["args"][0]["bytes"].ToString().Equals(secretHash.ToHexString()) &&
                       Params["args"][0]["args"][0]["args"][1]["args"][0]["args"][1]["int"].ToObject<long>() == refundTimestamp &&
                       Params["args"][0]["args"][0]["args"][0]["string"].ToString().Equals(participant);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool IsSwapAdd(byte[] secretHash)
        {
            try
            {
                return Params["args"][0]["args"][0]["bytes"].ToString().Equals(secretHash.ToHexString());
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool IsSwapRedeem(byte[] secretHash)
        {
            try
            {
                var secretBytes = Hex.FromString(Params["args"][0]["args"][0]["bytes"].ToString());
                var secretHashBytes = CurrencySwap.CreateSwapSecretHash(secretBytes);

                return secretHashBytes.SequenceEqual(secretHash);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool IsSwapRefund(byte[] secretHash)
        {
            try
            {
                var secretHashBytes = Hex.FromString(Params["args"][0]["args"][0]["bytes"].ToString());

                return secretHashBytes.SequenceEqual(secretHash);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public byte[] GetSecret()
        {
            return Hex.FromString(Params["args"][0]["args"][0]["bytes"].ToString());
        }

        public decimal GetRedeemFee()
        {
            return decimal.Parse(Params["args"][0]["args"][0]["args"][1]["args"][1]["int"].ToString());
        }
    }
}