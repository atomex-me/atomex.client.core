using System.Collections.Generic;
using System.Linq;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;

namespace Atomex.Common
{
    public static class TxOutputExtensions
    {
        public static IEnumerable<BitcoinBasedTxOutput> SelectOutputsForAmount(
            this IEnumerable<BitcoinBasedTxOutput> outputs,
            long amountInSatoshi)
        {
            foreach (var selectedOutputs in outputs.SelectOutputs())
            {
                var selectedAmountInSatoshi = selectedOutputs.Sum(o => o.Value);

                if (selectedAmountInSatoshi >= amountInSatoshi)
                    return selectedOutputs;
            }

            return Enumerable.Empty<BitcoinBasedTxOutput>();
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

        public static IEnumerable<IEnumerable<BitcoinBasedTxOutput>> SelectOutputs(this IEnumerable<BitcoinBasedTxOutput> outputs)
        {
            var outputsList = outputs.ToList();

            // sort ascending balance
            outputsList.Sort((o1, o2) => o1.Value.CompareTo(o2.Value));

            // single outputs
            foreach (var output in outputsList)
                yield return new BitcoinBasedTxOutput[] { output };

            // sort descending balance
            outputsList.Reverse();

            for (var i = 1; i <= outputsList.Count; ++i)
                yield return outputsList.Take(i);
        }
    }
}