using System.Linq;
using Atomix.Core.Entities;
using LiteDB;

namespace Atomix.Common.Bson
{
    public class CurrencyToBsonSerializer : BsonSerializer<Currency>
    {
        protected override Currency Deserialize(BsonValue bsonValue)
        {
            return Currencies.Available.FirstOrDefault(s => s.Name.Equals(bsonValue.AsString));
        }

        protected override BsonValue Serialize(Currency currency)
        {
            return currency.Name;
        }
    }
}