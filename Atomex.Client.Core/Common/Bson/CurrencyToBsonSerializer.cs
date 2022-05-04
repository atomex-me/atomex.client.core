using LiteDB;

using Atomex.Core;
using Atomex.Abstract;

namespace Atomex.Common.Bson
{
    public class CurrencyToBsonSerializer : BsonSerializer<CurrencyConfig_OLD>
    {
        private readonly ICurrencies _currencies;

        public CurrencyToBsonSerializer(ICurrencies currencies)
        {
            _currencies = currencies;
        }

        public override CurrencyConfig_OLD Deserialize(BsonValue bsonValue) =>
            _currencies.GetByName(bsonValue.AsString);

        public override BsonValue Serialize(CurrencyConfig_OLD currency) =>
            currency.Name;
    }
}