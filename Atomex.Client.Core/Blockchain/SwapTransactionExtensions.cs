using System;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;

namespace Atomex.Blockchain
{
    public static class SwapTransactionExtensions
    {
        public static byte[] ExtractSecret(this ITxPoint input)
        {
            if (input is BitcoinBasedTxPoint bitcoinBaseInput)
                return bitcoinBaseInput.ExtractSecret();

            throw new NotSupportedException("Input not supported");
        }
    }
}