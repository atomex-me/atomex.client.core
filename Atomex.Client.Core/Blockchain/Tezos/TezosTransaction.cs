using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography;
using Atomex.Wallet.Abstract;

namespace Atomex.Blockchain.Tezos
{
    public class TezosTransaction : IAddressBasedTransaction
    {
        private const int DefaultConfirmations = 1;

        public string Id { get; set; }
        public string UniqueId => $"{Id}:{Currency.Name}";
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
        public decimal GasUsed { get; set; }
        public decimal StorageLimit { get; set; }
        public decimal Burn { get; set; }
        public string Alias { get; set; }

        public JObject Params { get; set; }
        public bool IsInternal { get; set; }
        public int InternalIndex { get; set; }

        public JArray Operations { get; private set; }
        public JObject Head { get; private set; }
        public SignedMessage SignedMessage { get; private set; }

        public string OperationType { get; set; }
        public bool UseSafeStorageLimit { get; set; } = false;
        public bool UseRun { get; set; } = true;
        public bool UsePreApply { get; set; } = true;
        public bool UseOfflineCounter { get; set; } = true;

        public List<TezosTransaction> InternalTxs { get; set; }

        public TezosTransaction Clone()
        {
            var resTx = new TezosTransaction()
            {
                Id           = Id,
                Currency     = Currency,
                State        = State,
                Type         = Type,
                CreationTime = CreationTime,

                From         = From,
                To           = To,
                Amount       = Amount,
                Fee          = Fee,
                GasLimit     = GasLimit,
                GasUsed      = GasUsed,
                StorageLimit = StorageLimit,
                Burn         = Burn,
                Alias        = Alias,

                Params        = Params,
                IsInternal    = IsInternal,
                InternalIndex = InternalIndex,
                InternalTxs   = new List<TezosTransaction>(),

                BlockInfo = (BlockInfo)(BlockInfo?.Clone() ?? null)
            };

            if (InternalTxs != null)
                foreach (var intTx in InternalTxs)
                    resTx.InternalTxs.Add(intTx.Clone());

            return resTx;
        }

        public async Task<bool> SignAsync(
            IKeyStorage keyStorage,
            WalletAddress address,
            CancellationToken cancellationToken = default)
        {
            if (address.KeyIndex == null)
            {
                Log.Error("Can't find private key for address {@address}", address);
                return false;
            }

            using var securePrivateKey = keyStorage
                .GetPrivateKey(Currency, address.KeyIndex);

            if (securePrivateKey == null)
            {
                Log.Error("Can't find private key for address {@address}", address);
                return false;
            }

            using var privateKey = securePrivateKey.ToUnsecuredBytes();

            if (Head == null)
            {
                var xtz = (Atomex.Tezos)Currency;

                var rpc = new Rpc(xtz.RpcNodeUri);

                Head = await rpc
                    .GetHeader()
                    .ConfigureAwait(false);
            }

            var forgedOpGroup = Forge.ForgeOperationsLocal(Head, Operations);

            SignedMessage = TezosSigner.SignHash(
                data: Hex.FromString(forgedOpGroup.ToString()),
                privateKey: privateKey,
                watermark: Watermark.Generic,
                isExtendedKey: privateKey.Length == 64);

            return SignedMessage != null;
        }

        public async Task<bool> FillOperationsAsync(
            JObject head,
            SecureBytes securePublicKey,
            CancellationToken cancellationToken = default)
        {
            using var publicKey = securePublicKey.ToUnsecuredBytes();

            var xtz = (Atomex.Tezos)Currency;

            var rpc = new Rpc(xtz.RpcNodeUri);

            var managerKey = await rpc
                .GetManagerKey(From)
                .ConfigureAwait(false);

            Head = head ?? await rpc
                .GetHeader()
                .ConfigureAwait(false);

            Operations = new JArray();

            var gas     = GasLimit.ToString(CultureInfo.InvariantCulture);
            var storage = StorageLimit.ToString(CultureInfo.InvariantCulture);

            var revealed = managerKey.Value<string>() != null;

            var counter = await TezosCounter.Instance
                .GetCounter(
                    address: From,
                    head: head,
                    tezosConfig: xtz,
                    useOffline: UseOfflineCounter,
                    counters: revealed ? 1 : 2)
                .ConfigureAwait(false);

            if (!revealed)
            {
                var revealOp = new JObject
                {
                    ["kind"]          = Internal.OperationType.Reveal,
                    ["fee"]           = "0",
                    ["public_key"]    = Base58Check.Encode(publicKey, Prefix.Edpk),
                    ["source"]        = From,
                    ["storage_limit"] = "0",
                    ["gas_limit"]     = xtz.RevealGasLimit.ToString(),
                    ["counter"]       = counter.ToString()
                };

                Operations.AddFirst(revealOp);

                counter++;
            }

            var operation = new JObject
            {
                ["kind"]          = OperationType,
                ["source"]        = From,
                ["fee"]           = ((int)Fee).ToString(CultureInfo.InvariantCulture),
                ["counter"]       = counter.ToString(),
                ["gas_limit"]     = gas,
                ["storage_limit"] = storage,
            };

            if (OperationType == Internal.OperationType.Transaction)
            {
                operation["amount"]      = Math.Round(Amount, 0).ToString(CultureInfo.InvariantCulture);
                operation["destination"] = To;
            }
            else if (OperationType == Internal.OperationType.Delegation)
            {
                operation["delegate"] = To;
            }
            else throw new NotSupportedException($"Operation type {OperationType} not supporeted yet.");

            Operations.Add(operation);

            if (Params != null)
                operation["parameters"] = Params;

            if (UseRun)
            {
                var fill = await rpc
                    .AutoFillOperations(xtz, Head, Operations, UseSafeStorageLimit)
                    .ConfigureAwait(false);

                if (!fill)
                {
                    Log.Error("Operation autofilling error");
                    return false;
                }

                Fee = Operations.Last["fee"].Value<decimal>().ToTez();
            }

            return true;
        }

        //public async Task<bool> AutoFillDelegationFeeAsync(
        //    IKeyStorage keyStorage,
        //    WalletAddress address,
        //    bool useDefaultFee)
        //{
        //    var xtz = (Atomex.Tezos) Currency;

        //    if (address.KeyIndex == null)
        //    {
        //        Log.Error("Can't find private key for address {@address}", address);
        //        return false;
        //    }

        //    using var securePublicKey = keyStorage
        //        .GetPublicKey(Currency, address.KeyIndex);

        //    using var publicKey = securePublicKey.ToUnsecuredBytes();

        //    var rpc = new Rpc(xtz.RpcNodeUri);

        //    Head = await rpc
        //        .GetHeader()
        //        .ConfigureAwait(false);

        //    var managerKey = await rpc
        //        .GetManagerKey(From)
        //        .ConfigureAwait(false);

        //    Operations = new JArray();

        //    var gas = GasLimit.ToString(CultureInfo.InvariantCulture);
        //    var storage = StorageLimit.ToString(CultureInfo.InvariantCulture);

        //    var counter = await TezosCounter.Instance
        //        .GetCounter(xtz, From, Head, ignoreCache: true)
        //        .ConfigureAwait(false);

        //    if (managerKey.Value<string>() == null)
        //    {
        //        var revealOp = new JObject
        //        {
        //            ["kind"]          = OperationType.Reveal,
        //            ["fee"]           = "0",
        //            ["public_key"]    = Base58Check.Encode(publicKey, Prefix.Edpk),
        //            ["source"]        = From,
        //            ["storage_limit"] = storage,
        //            ["gas_limit"]     = gas,
        //            ["counter"]       = counter.ToString()
        //        };

        //        Operations.AddFirst(revealOp);

        //        counter++;
        //    }

        //    var transaction = new JObject
        //    {
        //        ["kind"]          = OperationType.Delegation,
        //        ["source"]        = From,
        //        ["fee"]           = ((int)Fee).ToString(CultureInfo.InvariantCulture),
        //        ["counter"]       = counter.ToString(),
        //        ["gas_limit"]     = gas,
        //        ["storage_limit"] = storage,
        //        ["delegate"]      = To
        //    };

        //    Operations.Add(transaction);

        //    if (Params != null)
        //        transaction["parameters"] = Params;

        //    var fill = await rpc
        //        .AutoFillOperations(xtz, Head, Operations, useDefaultFee)
        //        .ConfigureAwait(false);

        //    if (!fill)
        //    {
        //        Log.Error("Delegation autofilling error");
        //        return false;
        //    }

        //    Fee = Operations.Last["fee"].Value<decimal>() / 1_000_000;
            
        //    return true;
        //}
    }
}