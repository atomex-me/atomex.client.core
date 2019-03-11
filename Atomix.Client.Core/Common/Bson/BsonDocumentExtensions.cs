using System;
using System.Linq;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Blockchain.Ethereum;
using LiteDB;

namespace Atomix.Common.Bson
{
    public static class BsonDocumentExtensions
    {
        private const string CurrencyKey = "currency";

        public static Type OutputType(this BsonDocument document)
        {
            var currencyName = document[CurrencyKey].IsString
                ? document[CurrencyKey].AsString
                : string.Empty;

            var currency = Currencies.Available.FirstOrDefault(c => c.Name.Equals(currencyName));

            if (currency == null)
                throw new Exception($"Currency with name {currencyName} not found");

            if (currency is BitcoinBasedCurrency)
                return typeof(BitcoinBasedTxOutput);

            throw new NotSupportedException($"Not supported currency {currency.Name}");
        }
    }
}