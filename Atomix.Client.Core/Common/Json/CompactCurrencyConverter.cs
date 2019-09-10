using System;
using System.Collections.Generic;
using System.Linq;
using Atomix.Core.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Atomix.Common.Json
{
    public class CompactCurrencyConverter : JsonConverter
    {
        public override bool CanWrite => true;
        public override bool CanRead => Currencies != null;

        public IEnumerable<Currency> Currencies { get; }

        public CompactCurrencyConverter()
            : this(null)
        {
        }

        public CompactCurrencyConverter(
            IEnumerable<Currency> currencies)
        {
            Currencies = currencies;
        }

        public override void WriteJson(
            JsonWriter writer,
            object value,
            JsonSerializer serializer)
        {
            var currency = (Currency)value;

            var @object = new JObject
            {
                [nameof(Currency.Name)] = currency?.Name
            };

            @object.WriteTo(writer);
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            var token = JToken.Load(reader);

            if (token.Type != JTokenType.Object)
                throw new InvalidOperationException("Invalid json");

            var @object = (JObject)token;

            if (!@object.ContainsKey(nameof(Currency.Name)))
                throw new InvalidOperationException("Invalid json format");

            var currency = @object[nameof(Currency.Name)].Value<string>();

            return Currencies.FirstOrDefault(s => s.Name.Equals(currency));
        }

        public override bool CanConvert(
            Type objectType)
        {
            return typeof(Currency).IsAssignableFrom(objectType);
        }
    }
}