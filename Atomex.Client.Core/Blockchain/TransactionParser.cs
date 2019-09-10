using System;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Core.Entities;
using Serilog;

namespace Atomex.Blockchain
{
    public class TransactionParser
    {
        public static IBlockchainTransaction ParseTransaction(Currency currency, byte[] txBytes)
        {
            if (currency is BitcoinBasedCurrency btcBaseCurrency)
                return new BitcoinBasedTransaction(btcBaseCurrency, txBytes);

            throw new NotSupportedException($"Currency {currency.Name} not supported");
        }

        public static bool TryParseTransaction(Currency currency, byte[] txBytes, out IBlockchainTransaction tx)
        {
            tx = null;

            try
            {
                tx = ParseTransaction(currency, txBytes);
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