using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Netezos.Forging;
using Netezos.Forging.Models;

using Atomex.Blockchain.Abstract;
using Operation = Atomex.Blockchain.Tezos.Operations.Operation;

namespace Atomex.Blockchain.Tezos
{
    public enum TezosOperationType
    {
        Transaction,
        Delegation
    }

    public class TezosOperation : Transaction
    {
        private readonly IEnumerable<OperationContent> _operationsContents;

        public override string TxId { get; set; }
        public override string Currency { get; set; } = TezosHelper.Xtz;
        public override TransactionStatus Status { get; set; }
        public override DateTimeOffset? CreationTime { get; set; }
        public override DateTimeOffset? BlockTime { get; set; }
        public override long BlockHeight { get; set; }
        public override long Confirmations { get; set; }

        public string From => Operations.FirstOrDefault()?.Sender?.Address;
        public string Branch { get; set; }
        public byte[] Signature { get; set; }
        public IEnumerable<Operation> Operations { get; }

        public TezosOperation(
            OperationContent operationContent,
            string branch)
            : this(new OperationContent[]{ operationContent }, branch) 
        {
        }

        public TezosOperation(
            IEnumerable<OperationContent> operationsContents,
            string branch)
        {
            if (operationsContents == null)
                throw new ArgumentNullException(nameof(operationsContents));

            if (!operationsContents.Any())
                throw new ArgumentException("At least one operation content is required", nameof(operationsContents));

            _operationsContents = operationsContents;

            Branch = branch ?? throw new ArgumentNullException(nameof(branch));

            CreationTime = DateTimeOffset.UtcNow;
        }

        public TezosOperation(
            IEnumerable<Operation> operations,
            int recentBlockLevel = 0)
        {
            if (operations == null)
                throw new ArgumentNullException(nameof(operations));

            if (!operations.Any())
                throw new ArgumentException("At least one operation is required", nameof(operations));

            Operations = operations;

            var firstOperation = Operations.First();

            TxId          = firstOperation.Hash;
            Status        = firstOperation.Status.ParseOperationStatus();
            CreationTime  = firstOperation.BlockTime;
            BlockTime     = firstOperation.BlockTime;
            BlockHeight   = firstOperation.BlockLevel;
            Branch        = firstOperation.Block;

            Confirmations = recentBlockLevel != 0
                ? recentBlockLevel - firstOperation.BlockLevel
                : Math.Max((long)(DateTimeOffset.UtcNow - firstOperation.BlockTime).TotalMinutes, 0); // approximate confirmations
        }

        public async Task<byte[]> ForgeAsync(
            bool addOperationPrefix = false)
        {
            byte[] forgedOperation = null;

            if (_operationsContents.Any(o => o is ManagerOperationContent))
            {
                forgedOperation = await new LocalForge()
                    .ForgeOperationGroupAsync(
                        branch: Branch,
                        contents: _operationsContents.Cast<ManagerOperationContent>())
                    .ConfigureAwait(false);
            }
            else if (Operations.Count() == 1)
            {
                forgedOperation = await new LocalForge()
                    .ForgeOperationAsync(
                        branch: Branch,
                        content: _operationsContents.First())
                    .ConfigureAwait(false);
            }
            else throw new NotSupportedException("Can't forge several non manager operatrions");

            return addOperationPrefix
                ? new byte[] { 3 }
                    .Concat(forgedOperation)
                    .ToArray()
                : forgedOperation;
        }
    }
}