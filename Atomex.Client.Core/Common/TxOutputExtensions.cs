using System.Collections.Generic;
using System.Linq;

using Atomex.Blockchain.Bitcoin;

namespace Atomex.Common
{
    public static class TxOutputExtensions
    {
        public static IEnumerable<BitcoinTxOutput> SelectOutputsForAmount(
            this IEnumerable<BitcoinTxOutput> outputs,
            long amountInSatoshi)
        {
            foreach (var selectedOutputs in outputs.SelectOutputs())
            {
                var selectedAmountInSatoshi = selectedOutputs.Sum(o => o.Value);

                if (selectedAmountInSatoshi >= amountInSatoshi)
                    return selectedOutputs;
            }

            return Enumerable.Empty<BitcoinTxOutput>();
        }

        public static IEnumerable<BitcoinTxOutput> RemoveDuplicates(
            this IEnumerable<BitcoinTxOutput> outputs)
        {
            return outputs.GroupBy(o => $"{o.TxId}{o.Index}", RemoveDuplicatesOutputs);
        }

        private static BitcoinTxOutput RemoveDuplicatesOutputs(
            string id,
            IEnumerable<BitcoinTxOutput> outputs)
        {
            var txOutputs = outputs.ToList();

            return txOutputs.FirstOrDefault(o => o.IsSpent) ?? txOutputs.First();
        }

        public static IEnumerable<IEnumerable<BitcoinTxOutput>> SelectOutputs(this IEnumerable<BitcoinTxOutput> outputs)
        {
            var outputsList = outputs.ToList();

            // sort ascending balance
            outputsList.Sort((o1, o2) => o1.Value.CompareTo(o2.Value));

            // single outputs
            foreach (var output in outputsList)
                yield return new BitcoinTxOutput[] { output };

            // sort descending balance
            outputsList.Reverse();

            for (var i = 1; i <= outputsList.Count; ++i)
                yield return outputsList.Take(i);
        }
    }
}