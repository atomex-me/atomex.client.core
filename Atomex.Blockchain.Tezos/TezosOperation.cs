using System;
using System.Collections.Generic;
using System.Linq;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Common;
using Atomex.Blockchain.Tezos.Tzkt.Operations;

namespace Atomex.Blockchain.Tezos
{
    public class TezosOperation : ITransaction
    {
        public string Id { get; set; }
        public string Currency { get; set; }
        public TransactionStatus Status { get; set; }
        public TransactionType Type { get; set; }
        public DateTimeOffset? CreationTime { get; set; }
        public DateTimeOffset? BlockTime { get; set; }
        public long BlockHeight { get; set; }
        public long Confirmations { get; set; }
        public bool IsConfirmed => Confirmations > 0;
        public bool IsTypeResolved => Type != TransactionType.Unknown;
        public string? From => Operations.FirstOrDefault()?.Sender?.Address;

        public IEnumerable<Operation> Operations { get; }
        public MichelineFormat ParametersFormat { get; }

        public TezosOperation(
            IEnumerable<Operation> operations,
            MichelineFormat operationParametersFormat,
            int recentBlockLevel = 0)
        {
            if (operations == null)
                throw new ArgumentNullException(nameof(operations));

            if (!operations.Any())
                throw new ArgumentException("At least one operation is required", nameof(operations));

            Operations = operations;
            ParametersFormat = operationParametersFormat;

            var firstOperation = Operations.First();

            Id           = firstOperation.Hash;
            Currency     = TezosHelper.Xtz;
            Status       = firstOperation.Status.ParseOperationStatus();
            CreationTime = firstOperation.BlockTime;
            BlockTime    = firstOperation.BlockTime;
            BlockHeight  = firstOperation.BlockLevel;

            Confirmations = recentBlockLevel != 0
                ? recentBlockLevel - firstOperation.BlockLevel
                : Math.Max((long)(DateTimeOffset.UtcNow - firstOperation.BlockTime).TotalMinutes, 0); // approximate confirmations
        }

        public TezosOperation(
            TezosOperationRequest operationRequest,
            string operationId)
        {
            Id            = operationId;
            Currency      = TezosHelper.Xtz;
            Status        = TransactionStatus.Pending;
            CreationTime  = DateTimeOffset.UtcNow;
            BlockTime     = null;
            BlockHeight   = 0;
            Confirmations = 0;

            Operations = operationRequest.OperationsContents.Select(oc => )
        }

        public bool IsManaged() => Operations
            ?.Any(o => o is ManagerOperation) ?? false;

        //public string Id { get; set; }
        //public string UniqueId => $"{Id}:{Currency}";
        //public string Currency { get; set; }
        //public BlockInfo BlockInfo { get; set; }
        //public TransactionStatus Status { get; set ; }
        //public TransactionType Type { get; set; }
        //public DateTime? CreationTime { get; set; }
        //public bool IsConfirmed => BlockInfo?.Confirmations >= DefaultConfirmations;

        //public string From { get; set; }
        //public string To { get; set; }
        //public decimal Amount { get; set; }
        //public decimal Fee { get; set; }
        //public decimal GasLimit { get; set; }
        //public decimal GasUsed { get; set; }
        //public decimal StorageLimit { get; set; }
        //public decimal StorageUsed { get; set; }
        //public decimal Burn { get; set; }
        //public string Alias { get; set; }

        //public JObject Params { get; set; }
        //public bool IsInternal { get; set; }
        //public int InternalIndex { get; set; }

        //[BsonIgnore]
        //public JObject Head { get; set; }
        //[BsonIgnore]
        //public JArray Operations { get; private set; }
        //[BsonIgnore]
        //public SignedMessage SignedMessage { get; set; }

        //public string OperationType { get; set; } = Internal.OperationType.Transaction;
        //[BsonIgnore]
        //public bool UseSafeStorageLimit { get; set; } = false;
        //[BsonIgnore]
        //public bool UseRun { get; set; } = true;

        //public bool UsePreApply { get; set; } = false;
        //public bool UseOfflineCounter { get; set; } = true;
        //public int UsedCounters { get; set; }

        //public List<TezosOperation> InternalTxs { get; set; }

        //public TezosOperation Clone()
        //{
        //    var resTx = new TezosOperation()
        //    {
        //        Id           = Id,
        //        Currency     = Currency,
        //        Status        = Status,
        //        Type         = Type,
        //        CreationTime = CreationTime,

        //        From         = From,
        //        To           = To,
        //        Amount       = Amount,
        //        Fee          = Fee,
        //        GasLimit     = GasLimit,
        //        GasUsed      = GasUsed,
        //        StorageLimit = StorageLimit,
        //        StorageUsed  = StorageUsed,
        //        Burn         = Burn,
        //        Alias        = Alias,

        //        Params        = Params,
        //        IsInternal    = IsInternal,
        //        InternalIndex = InternalIndex,
        //        InternalTxs   = new List<TezosOperation>(),

        //        BlockInfo = (BlockInfo)(BlockInfo?.Clone() ?? null)
        //    };

        //    if (InternalTxs != null)
        //        foreach (var intTx in InternalTxs)
        //            resTx.InternalTxs.Add(intTx.Clone());

        //    return resTx;
        //}

        //public async Task<(bool result, bool isRunSuccess, bool hasReveal)> FillOperationsAsync(
        //    SecureBytes securePublicKey,
        //    TezosConfig tezosConfig,
        //    int headOffset = 0,
        //    bool isAlreadyRevealed = false,
        //    CancellationToken cancellationToken = default)
        //{
        //    var publicKey = securePublicKey.ToUnsecuredBytes();

        //    var rpc = new Rpc(tezosConfig.RpcNodeUri);

        //    var managerKey = await rpc
        //        .GetManagerKey(From)
        //        .ConfigureAwait(false);

        //    var actualHead = await rpc
        //        .GetHeader()
        //        .ConfigureAwait(false);

        //    if (Head == null)
        //        Head = await rpc
        //            .GetHeader(headOffset)
        //            .ConfigureAwait(false);

        //    Operations = new JArray();

        //    var gas      = GasLimit.ToString(CultureInfo.InvariantCulture);
        //    var storage  = StorageLimit.ToString(CultureInfo.InvariantCulture);
        //    var revealed = managerKey.Value<string>() != null || isAlreadyRevealed;

        //    UsedCounters = revealed ? 1 : 2;

        //    var counter = UseOfflineCounter
        //        ? await TezosCounter.Instance
        //            .GetOfflineCounterAsync(
        //                address: From,
        //                head: actualHead["hash"].ToString(),
        //                rpcNodeUri: tezosConfig.RpcNodeUri,
        //                numberOfCounters: UsedCounters)
        //            .ConfigureAwait(false)
        //        : await TezosCounter.Instance
        //            .GetCounterAsync(
        //                address: From,
        //                head: actualHead["hash"].ToString(),
        //                rpcNodeUri: tezosConfig.RpcNodeUri)
        //            .ConfigureAwait(false);

        //    if (!revealed)
        //    {
        //        var revealOp = new JObject
        //        {
        //            ["kind"]          = Internal.OperationType.Reveal,
        //            ["fee"]           = "0",
        //            ["public_key"]    = Base58Check.Encode(publicKey, Prefix.Edpk),
        //            ["source"]        = From,
        //            ["storage_limit"] = "0",
        //            ["gas_limit"]     = tezosConfig.RevealGasLimit.ToString(),
        //            ["counter"]       = counter.ToString()
        //        };

        //        Operations.AddFirst(revealOp);

        //        counter++;
        //    }

        //    var operation = new JObject
        //    {
        //        ["kind"]          = OperationType,
        //        ["source"]        = From,
        //        ["fee"]           = ((int)Fee).ToString(CultureInfo.InvariantCulture),
        //        ["counter"]       = counter.ToString(),
        //        ["gas_limit"]     = gas,
        //        ["storage_limit"] = storage,
        //    };

        //    if (OperationType == Internal.OperationType.Transaction)
        //    {
        //        operation["amount"]      = Math.Round(Amount, 0).ToString(CultureInfo.InvariantCulture);
        //        operation["destination"] = To;
        //    }
        //    else if (OperationType == Internal.OperationType.Delegation)
        //    {
        //        if (To != null)
        //            operation["delegate"] = To;
        //    }
        //    else throw new NotSupportedException($"Operation type {OperationType} not supporeted yet.");

        //    Operations.Add(operation);

        //    if (Params != null)
        //        operation["parameters"] = Params;

        //    var isRunSuccess = false;

        //    if (UseRun)
        //    {
        //        var fill = await rpc
        //            .AutoFillOperations(tezosConfig, Head, Operations, UseSafeStorageLimit)
        //            .ConfigureAwait(false);

        //        if (!fill)
        //        {
        //            Log.Warning("Operation autofilling error");
        //        }
        //        else
        //        {
        //            Fee = Operations.Last["fee"].Value<decimal>().ToTez();
        //            isRunSuccess = true;
        //        }
        //    }

        //    return (
        //        result: true,
        //        isRunSuccess: isRunSuccess,
        //        hasReveal: !revealed
        //    );
        //}

        //public void RollbackOfflineCounterIfNeed()
        //{
        //    if (UseOfflineCounter)
        //        TezosCounter.Instance.RollbackOfflineCounter(From, UsedCounters);
        //}
    }
}