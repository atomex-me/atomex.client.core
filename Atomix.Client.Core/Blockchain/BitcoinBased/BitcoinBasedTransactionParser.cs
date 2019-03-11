using Serilog;
using System;

namespace Atomix.Blockchain.BitcoinBased
{
    public class BitcoinBasedTransactionParser
    {
        public static bool TryParseTransaction(
            BitcoinBasedCurrency currency,
            byte[] txBytes,
            out IBitcoinBasedTransaction tx)
        {
            tx = null;

            try
            {
                tx = new BitcoinBasedTransaction(currency, txBytes);
                return true;
            }
            catch (Exception e)
            {
                Log.Error(e, "Transaction parse error: {@message}", e.Message);
            }

            return false;
        }
    }
}