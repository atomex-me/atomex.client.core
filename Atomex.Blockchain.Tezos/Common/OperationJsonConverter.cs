using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Atomex.Blockchain.Tezos.Operations;

namespace Atomex.Blockchain.Tezos.Common
{
    public class OperationJsonConverter : JsonConverter<Operation>
    {
        public override Operation Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (!JsonDocument.TryParseValue(ref reader, out var document))
                throw new JsonException("Failed to parse JsonDocument");

            if (!document.RootElement.TryGetProperty("type", out var type))
                throw new JsonException("Failed to extract type property, it might be missing?");

            var operationType = type.GetString();
            var rawJson = document.RootElement.GetRawText();

            return operationType switch
            {
                "endorsement"        => JsonSerializer.Deserialize<EndorsmentOperation>(rawJson, options),
                "ballot"             => JsonSerializer.Deserialize<BallotOperation>(rawJson, options),
                "proposal"           => JsonSerializer.Deserialize<ProposalOperation>(rawJson, options),
                "activation"         => JsonSerializer.Deserialize<ActivationOperation>(rawJson, options),
                "double_baking"      => JsonSerializer.Deserialize<DoubleBakingOperation>(rawJson, options),
                "double_endorsing"   => JsonSerializer.Deserialize<DoubleEndorsingOperation>(rawJson, options),
                "nonce_revelation"   => JsonSerializer.Deserialize<NonceRevelationOperation>(rawJson, options),
                "delegation"         => JsonSerializer.Deserialize<DelegationOperation>(rawJson, options),
                "origination"        => JsonSerializer.Deserialize<OriginationOperation>(rawJson, options),
                "transaction"        => JsonSerializer.Deserialize<TransactionOperation>(rawJson, options),
                "reveal"             => JsonSerializer.Deserialize<RevealOperation>(rawJson, options),
                "migration"          => JsonSerializer.Deserialize<MigrationOperation>(rawJson, options),
                "revelation_penalty" => JsonSerializer.Deserialize<RevelationPenaltyOperation>(rawJson, options),
                "baking"             => JsonSerializer.Deserialize<BakingOperation>(rawJson, options),
                _ => throw new JsonException($"{operationType} not supported yet!")
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            Operation value,
            JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}