using System.Text.Json;

using Netezos.Encoding;

using Atomex.Blockchain.Tezos.Operations;

namespace Atomex.Blockchain.Tezos.Common
{
    public enum MichelineFormat
    {
        Json = 0,
        JsonString = 1,
        RawMicheline = 2,
        RawMichelineString = 3
    }

    public static class MichelineExtensions
    {
        public static IMicheline? TryParseMicheline(
            Parameter parameter,
            string parameters)
        {
            if (parameters != null)
                return TryExtractMichelineValue(parameters);

            if (parameter.Value is not JsonElement value)
                return null;

            try
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    return Micheline.FromJson(value.GetRawText());
                }
                else if (value.ValueKind == JsonValueKind.String)
                {
                    return Micheline.FromJson(value.GetString());
                }
                else return null;
            }
            catch
            {
                return null;
            }
        }

        public static string? ExtractMichelineValue(string parameters)
        {
            const string valueKey = "\"value\":";

            var valueKeyIndex = parameters.IndexOf(valueKey);

            if (valueKeyIndex == -1)
                return null;

            var valueIndex = valueKeyIndex + valueKey.Length;

            return parameters.Substring(valueIndex, parameters.Length - valueIndex - 1);
        }

        public static IMicheline? TryExtractMichelineValue(string parameters)
        {
            var michelineValueJson = ExtractMichelineValue(parameters);

            return michelineValueJson != null
                ? Micheline.FromJson(michelineValueJson)
                : null;
        }
    }
}