using System;
using System.Collections.Generic;
using System.Linq;
using Atomix.Core.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Atomix.Common.Json
{
    public class CompactSymbolConverter : JsonConverter
    {
        public override bool CanWrite => true;
        public override bool CanRead => Symbols != null;

        public IEnumerable<Symbol> Symbols { get; }

        public CompactSymbolConverter()
            : this(null)
        {
        }

        public CompactSymbolConverter(IEnumerable<Symbol> symbols)
        {
            Symbols = symbols;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var symbol = (Symbol)value;

            var @object = new JObject
            {
                [nameof(Symbol.Name)] = symbol?.Name
            };

            @object.WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            var token = JToken.Load(reader);

            if (token.Type != JTokenType.Object)
                throw new InvalidOperationException("Invalid json");

            var @object = (JObject) token;

            if  (!@object.ContainsKey(nameof(Symbol.Name)))
                throw new InvalidOperationException("Invalid json format");

            var symbol = @object[nameof(Symbol.Name)].Value<string>();

            return Symbols.FirstOrDefault(s => s.Name.Equals(symbol));
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(Symbol).IsAssignableFrom(objectType);
        }
    }
}