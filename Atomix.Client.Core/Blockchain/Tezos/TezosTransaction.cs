using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.Tezos.Internal;
using Atomix.Common;
using Atomix.Core.Entities;
using Atomix.Cryptography;
using Atomix.Swaps;
using Atomix.Wallet.Abstract;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomix.Blockchain.Tezos
{
    public class TezosTransaction : IAddressBasedTransaction
    {
        public const int InputTransaction = 1;
        public const int OutputTransaction = 2;
        public const int SelfTransaction = 3;
        public const int ActivateAccountTransaction = 4;
        public const int DefaultConfirmations = 1;
        
        public string Id { get; set; }
        public Currency Currency => Currencies.Xtz;
        public BlockInfo BlockInfo { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public decimal Amount { get; set; }
        public decimal Fee { get; set; }
        public decimal GasLimit { get; set; }
        public decimal StorageLimit { get; set; }
        public JObject Params { get; set; }
        public int Type { get; set; }
        public bool IsInternal { get; set; }

        public JArray Operations { get; set; }
        public JObject Head { get; set; }
        public SignedMessage SignedMessage { get; set; }

        public bool IsConfirmed() => BlockInfo?.Confirmations >= DefaultConfirmations;

        public TezosTransaction()
        {
            BlockInfo = new BlockInfo()
            {
                FirstSeen = DateTime.UtcNow
            };
        }

        public async Task<bool> SignAsync(
            IPrivateKeyStorage keyStorage,
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var keyIndex = await keyStorage
                .RecoverKeyIndexAsync(Currency, address, cancellationToken)
                .ConfigureAwait(false);

            if (keyIndex == null)
            {
                Log.Error($"Can't find private key for address {address}");
                return false;
            }

            var privateKey = keyStorage.GetPrivateKey(Currency, keyIndex);
            var publicKey = keyStorage.GetPublicKey(Currency, keyIndex);

            var rpc = new Rpc(Currencies.Xtz.RpcProvider);

            Head = await rpc
                .GetHeader()
                .ConfigureAwait(false);

            var account = await rpc
                .GetAccountForBlock(Head["hash"].ToString(), From)
                .ConfigureAwait(false);

            var counter = int.Parse(account["counter"].ToString());

            var managerKey = await rpc
                .GetManagerKey(From)
                .ConfigureAwait(false);

            Operations = new JArray();

            var gas = GasLimit.ToString(CultureInfo.InvariantCulture);
            var storage = StorageLimit.ToString(CultureInfo.InvariantCulture);

            if (privateKey != null && managerKey["key"] == null)
            {
                var revealOp = new JObject
                {
                    ["kind"] = OperationType.Reveal,
                    ["fee"] = "0",
                    ["public_key"] = Base58Check.Encode(publicKey, Prefix.Edpk),
                    ["source"] = From,
                    ["storage_limit"] = storage,
                    ["gas_limit"] = gas,
                    ["counter"] = (++counter).ToString()
                };

                Operations.AddFirst(revealOp);
            }

            var transaction = new JObject
            {
                ["kind"] = OperationType.Transaction,
                ["source"] = From,
                ["fee"] = Fee.ToString(CultureInfo.InvariantCulture),
                ["counter"] = (++counter).ToString(),
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

            SignedMessage = new TezosSigner().SignHash(
                data: Hex.FromString(forgedOpGroup.ToString()),
                privateKey: privateKey,
                watermark: Watermark.Generic);

            return true;
        }

        public decimal AmountInXtz()
        {
            switch (Type)
            {
                case InputTransaction:
                    return Atomix.Tezos.MtzToTz(Amount);
                case OutputTransaction:
                    return -Atomix.Tezos.MtzToTz(Amount + Fee);
                case SelfTransaction:
                    return -Atomix.Tezos.MtzToTz(Fee);
                default:
                    return Atomix.Tezos.MtzToTz(Amount + Fee);
            }
        }

        public bool IsSwapPayment(long refundTime, byte[] secretHash, string participant)
        {
            try
            {
                return Params["args"][0]["args"][0]["int"].ToObject<long>() == refundTime &&
                       Params["args"][0]["args"][1]["args"][0]["bytes"].ToString().Equals(secretHash.ToHexString()) &&
                       Params["args"][0]["args"][1]["args"][1]["args"][0]["string"].ToString().Equals(participant);
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
                var secretHashHex = Params["args"][0]["args"][0]["args"][0]["bytes"].ToString();

                if (secretHashHex.Equals(secretHash.ToHexString()))
                {
                    var secretBytes = Hex.FromString(Params["args"][0]["args"][0]["args"][1]["bytes"].ToString());

                    return Swap.CreateSwapSecretHash(secretBytes).SequenceEqual(secretHash);
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public byte[] GetSecret()
        {
            return Hex.FromString(Params["args"][0]["args"][0]["args"][1]["bytes"].ToString());
        }
    }
}