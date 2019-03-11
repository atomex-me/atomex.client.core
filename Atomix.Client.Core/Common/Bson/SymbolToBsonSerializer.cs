using System.Linq;
using Atomix.Core.Entities;
using LiteDB;

namespace Atomix.Common.Bson
{
    public class SymbolToBsonSerializer : BsonSerializer<Symbol>
    {
        protected override Symbol Deserialize(BsonValue bsonValue)
        {
            return Symbols.Available.FirstOrDefault(s => s.Name.Equals(bsonValue.AsString));
        }

        protected override BsonValue Serialize(Symbol symbol)
        {
            return symbol.Name;
        }
    }
}