using System.Collections.Generic;
using System.Linq;
using Atomix.Core.Entities;
using LiteDB;

namespace Atomix.Common.Bson
{
    public class SymbolToBsonSerializer : BsonSerializer<Symbol>
    {
        private readonly IEnumerable<Symbol> _symbols;

        public SymbolToBsonSerializer(IEnumerable<Symbol> symbols)
        {
            _symbols = symbols;
        }

        public override Symbol Deserialize(BsonValue bsonValue)
        {
            return _symbols.FirstOrDefault(s => s.Name.Equals(bsonValue.AsString));
        }

        public override BsonValue Serialize(Symbol symbol)
        {
            return symbol.Name;
        }
    }
}