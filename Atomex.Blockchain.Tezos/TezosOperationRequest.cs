using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Netezos.Forging;
using Netezos.Forging.Models;

namespace Atomex.Blockchain.Tezos
{
    public class TezosOperationRequest
    {
        public string Branch { get; set; }
        public byte[]? Signature { get; set; }
        public string? From => (OperationsContents.FirstOrDefault(o => o is ManagerOperationContent) as ManagerOperationContent)?.Source;
        public IEnumerable<OperationContent> OperationsContents { get; }
        public bool IsAutoFilled { get; set; }

        public TezosOperationRequest(
            OperationContent operationContent,
            string branch,
            bool isAutoFilled)
            : this(new OperationContent[] { operationContent }, branch, isAutoFilled)
        {
        }

        public TezosOperationRequest(
            IEnumerable<OperationContent> operationsContents,
            string branch,
            bool isAutoFilled)
        {
            if (operationsContents == null)
                throw new ArgumentNullException(nameof(operationsContents));

            if (!operationsContents.Any())
                throw new ArgumentException("At least one operation content is required", nameof(operationsContents));

            OperationsContents = operationsContents;
            Branch = branch ?? throw new ArgumentNullException(nameof(branch));
            IsAutoFilled = isAutoFilled;
        }

        public async Task<byte[]> ForgeAsync()
        {
            byte[]? forgedOperation = null;

            if (OperationsContents.Any(o => o is ManagerOperationContent))
            {
                return forgedOperation = await new LocalForge()
                    .ForgeOperationGroupAsync(
                        branch: Branch,
                        contents: OperationsContents.Cast<ManagerOperationContent>())
                    .ConfigureAwait(false);
            }
            else if (OperationsContents.Count() == 1)
            {
                return forgedOperation = await new LocalForge()
                    .ForgeOperationAsync(
                        branch: Branch,
                        content: OperationsContents.First())
                    .ConfigureAwait(false);
            }
            else throw new NotSupportedException("Can't forge several non manager operations");
        }

        public bool IsManaged() => OperationsContents
            ?.Any(o => o is ManagerOperationContent) ?? false;

        public long TotalFee()
        {
            var fee = 0L;

            foreach (var op in OperationsContents)
            {
                fee += op switch
                {
                    ManagerOperationContent c => c.Fee,
                    _ => 0
                };
            }

            return fee;
        }

        public long TotalGasLimit()
        {
            var gasLimit = 0L;

            foreach (var op in OperationsContents)
            {
                gasLimit += op switch
                {
                    ManagerOperationContent c => c.GasLimit,
                    _ => 0
                };
            }

            return gasLimit;
        }

        public long TotalStorageLimit()
        {
            var storageLimit = 0L;

            foreach (var op in OperationsContents)
            {
                storageLimit += op switch
                {
                    ManagerOperationContent c => c.StorageLimit,
                    _ => 0
                };
            }

            return storageLimit;
        }
    }
}