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
        public string UniqueId => $"{Id}:{Currency}";
        public string Id { get; set; }
        public string Currency { get; set; }
        public TransactionStatus Status { get; set; }
        public DateTimeOffset? CreationTime { get; set; }
        public DateTimeOffset? BlockTime { get; set; }
        public long BlockHeight { get; set; }
        public long Confirmations { get; set; }
        public bool IsConfirmed => Confirmations > 0;
        public string? From => Operations.FirstOrDefault()?.Sender?.Address;

        public List<Operation> Operations { get; set; }
        public MichelineFormat ParametersFormat { get; set; }

        public TezosOperation()
        {
        }

        public TezosOperation(
            IEnumerable<Operation> operations,
            MichelineFormat operationParametersFormat,
            int recentBlockLevel = 0)
        {
            if (operations == null)
                throw new ArgumentNullException(nameof(operations));

            if (!operations.Any())
                throw new ArgumentException("At least one operation is required", nameof(operations));

            Operations = operations.ToList();
            ParametersFormat = operationParametersFormat;

            var firstOperation = Operations.First();

            Id            = firstOperation.Hash;
            Currency      = TezosHelper.Xtz;
            Status        = firstOperation.Status.ParseOperationStatus();
            CreationTime  = firstOperation.BlockTime;
            BlockTime     = firstOperation.BlockTime;
            BlockHeight   = firstOperation.BlockLevel;
            Confirmations = recentBlockLevel != 0
                ? recentBlockLevel - firstOperation.BlockLevel
                : Math.Max((long)(DateTimeOffset.UtcNow - firstOperation.BlockTime).TotalMinutes, 0); // approximate confirmations
        }

        public TezosOperation(
            TezosOperationRequest operationRequest,
            string operationId)
        {
            Operations = operationRequest.OperationsContents
                .Select(oc => oc.ToOperation(operationId))
                .ToList();
            ParametersFormat = MichelineFormat.RawMicheline;

            Id            = operationId;
            Currency      = TezosHelper.Xtz;
            Status        = TransactionStatus.Pending;
            CreationTime  = DateTimeOffset.UtcNow;
            BlockTime     = null;
            BlockHeight   = 0;
            Confirmations = 0;
        }

        public bool IsManaged() => Operations
            ?.Any(o => o is ManagerOperation) ?? false;
    }
}