using System.Collections.Generic;
using System.Linq;
using Atomex.Blockchain.Abstract;
using Atomex.Core.Entities;

namespace Atomex.Common
{
    public static class TxOutputExtensions
    {
        public static IEnumerable<ITxOutput> SelectOutputsForAmount(
            this IEnumerable<ITxOutput> outputs,
            long amount)
        {
            var usedOutputs = new List<ITxOutput>();
            var usedAmount = 0L;

            foreach (var output in outputs)
            {
                if (usedAmount >= amount)
                    break;

                usedOutputs.Add(output);
                usedAmount += output.Value;
            }

            return usedAmount >= amount
                ? usedOutputs
                : Enumerable.Empty<ITxOutput>();
        }

        public static IEnumerable<string> SelectAddressesForAmount(
            this IEnumerable<ITxOutput> outputs,
            Currency currency,
            long amount)
        {
            var usedAddresses = new HashSet<string>();
            var usedAmount = 0L;

            foreach (var output in outputs)
            {
                if (usedAmount >= amount)
                    break;

                var address = output.DestinationAddress(currency);

                if (!usedAddresses.Contains(address))
                    usedAddresses.Add(address);

                usedAmount += output.Value;
            }

            return usedAmount >= amount
                ? usedAddresses.ToList()
                : Enumerable.Empty<string>();
        }

        public static IEnumerable<ITxOutput> RemoveDuplicates(
            this IEnumerable<ITxOutput> outputs)
        {
            return outputs.GroupBy(o => $"{o.TxId}{o.Index}", RemoveDuplicatesOutputs);
        }

        private static ITxOutput RemoveDuplicatesOutputs(
            string id,
            IEnumerable<ITxOutput> outputs)
        {
            var txOutputs = outputs.ToList();

            return txOutputs.FirstOrDefault(o => o.IsSpent) ?? txOutputs.First();
        }
    }
}