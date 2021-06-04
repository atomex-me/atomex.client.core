using System.Collections.Generic;
using System.Linq;
using Atomex.Core;
using LiteDB;

namespace Atomex.Common.Bson
{
    public class CurrencyToBsonSerializer : BsonSerializer<CurrencyConfig>
    {
        private readonly IEnumerable<CurrencyConfig> _currencies;

        public CurrencyToBsonSerializer(IEnumerable<CurrencyConfig> currencies)
        {
            _currencies = currencies;
        }

        public override CurrencyConfig Deserialize(BsonValue bsonValue)
        {
            return _currencies.FirstOrDefault(s => s.Name == bsonValue.AsString);
        }

        public override BsonValue Serialize(CurrencyConfig currency)
        {
            return currency.Name;
        }
    }

}