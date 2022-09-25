using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Netezos.Forging;
using Netezos.Forging.Models;

namespace Atomex.Wallet.Tezos
{
    public static class TezosForge
    {
        public static async Task<byte[]> ForgeAsync(
            IEnumerable<OperationContent> operations,
            string branch,
            bool addOperationPrefix = false)
        {
            byte[] forgedOperation = null;

            if (operations.Any(o => o is ManagerOperationContent))
            {
                forgedOperation = await new LocalForge()
                    .ForgeOperationGroupAsync(
                        branch: branch,
                        contents: operations.Cast<ManagerOperationContent>())
                    .ConfigureAwait(false);
            }
            else if (operations.Count() == 1)
            {
                forgedOperation = await new LocalForge()
                    .ForgeOperationAsync(
                        branch: branch,
                        content: operations.First())
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