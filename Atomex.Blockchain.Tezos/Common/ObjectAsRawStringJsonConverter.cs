using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Common
{
    public class ObjectAsRawStringJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonSerializer.Deserialize<JsonDocument>(ref reader);

            return document.RootElement.GetRawText();
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}