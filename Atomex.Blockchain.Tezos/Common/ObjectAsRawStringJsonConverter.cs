using System;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Atomex.Blockchain.Tezos.Common
{
    public class ObjectAsRawStringJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => true;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return JObject.Load(reader).ToString(Formatting.None);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}