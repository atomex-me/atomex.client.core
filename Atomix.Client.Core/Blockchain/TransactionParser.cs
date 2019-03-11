using System;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Core.Entities;
using Serilog;

namespace Atomix.Blockchain
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