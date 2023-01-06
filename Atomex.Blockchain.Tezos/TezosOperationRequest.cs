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
        public byte[] Signature { get; set; }

        public IEnumerable<OperationContent> OperationsContents { get; }

        public TezosOperationRequest(
            OperationContent operationContent,
            string branch)
            : this(new OperationContent[] { operationContent }, branch)
        {
        }

        public TezosOperationRequest(
            IEnumerable<OperationContent> operationsContents,
            string branch)
        {
            if (operationsContents == null)
                throw new ArgumentNullException(nameof(operationsContents));

            if (!operationsContents.Any())
                throw new ArgumentException("At least one operation content is required", nameof(operationsContents));

            OperationsContents = operationsContents;
            Branch = branch ?? throw new ArgumentNullException(nameof(branch));
        }

        public async Task<byte[]> ForgeAsync(
            bool addOperationPrefix = false)
        {
            byte[]? forgedOperation = null;

            if (OperationsContents.Any(o => o is ManagerOperationContent))
            {
                forgedOperation = await new LocalForge()
                    .ForgeOperationGroupAsync(
                        branch: Branch,
                        contents: OperationsContents.Cast<ManagerOperationContent>())
                    .ConfigureAwait(false);
            }
            else if (OperationsContents.Count() == 1)
            {
                forgedOperation = await new LocalForge()
                    .ForgeOperationAsync(
                        branch: Branch,
                        content: OperationsContents.First())
                    .ConfigureAwait(false);
            }
            else throw new NotSupportedException("Can't forge several non manager operatrions");

            return addOperationPrefix
                ? new byte[] { 3 }
                    .Concat(forgedOperation)
                    .ToArray()
                : forgedOperation;
        }

        public bool IsManaged() => OperationsContents
            ?.Any(o => o is ManagerOperationContent) ?? false;
    }
}