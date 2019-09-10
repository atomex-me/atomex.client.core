using System;
using System.Collections.Generic;
using System.Linq;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;

namespace Atomex.Blockchain
{
    public static class SwapTransactionExtensions
    {
        public static IEnumerable<ITxOutput> SwapOutputs(this IInOutTransaction tx)
        {
            return tx.Outputs.Where(o => o.IsSwapPayment);
        }

        public static byte[] ExtractSecret(this ITxPoint input)
        {
            if (input is BitcoinBasedTxPoint bitcoinBaseInput)
                return bitcoinBaseInput.ExtractSecret();

            throw new NotSupportedException("Input not supported");
        }
    }
}