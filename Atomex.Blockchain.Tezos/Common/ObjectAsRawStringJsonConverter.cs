using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Tezos.Common
{
    public class ObjectAsRawStringJsonConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (!JsonDocument.TryParseValue(ref reader, out var document))
                throw new NotImplementedException();

            return document.RootElement.GetRawText();
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}