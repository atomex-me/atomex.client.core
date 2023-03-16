using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Atomex.Blockchain.Tezos.Tzkt.Operations;

namespace Atomex.Blockchain.Tezos.Common
{
    public class OperationJsonConverter : JsonConverter<Operation?>
    {
        public override Operation? Read(
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
                "transaction"                   => JsonSerializer.Deserialize<TransactionOperation>(rawJson, options),
                "reveal"                        => JsonSerializer.Deserialize<RevealOperation>(rawJson, options),
                "delegation"                    => JsonSerializer.Deserialize<DelegationOperation>(rawJson, options),
                "origination"                   => JsonSerializer.Deserialize<OriginationOperation>(rawJson, options),
                "activation"                    => JsonSerializer.Deserialize<ActivationOperation>(rawJson, options),

                "endorsement"                   => JsonSerializer.Deserialize<EndorsmentOperation>(rawJson, options),
                "preendorsement"                => JsonSerializer.Deserialize<PreendorsmentOperation>(rawJson, options),
                "ballot"                        => JsonSerializer.Deserialize<BallotOperation>(rawJson, options),
                "proposal"                      => JsonSerializer.Deserialize<ProposalOperation>(rawJson, options),
                "double_baking"                 => JsonSerializer.Deserialize<DoubleBakingOperation>(rawJson, options),
                "double_endorsing"              => JsonSerializer.Deserialize<DoubleEndorsingOperation>(rawJson, options),
                "double_preendorsing"           => JsonSerializer.Deserialize<DoublePreendorsingOperation>(rawJson, options),
                "nonce_revelation"              => JsonSerializer.Deserialize<NonceRevelationOperation>(rawJson, options),
                "vdf_revelation"                => JsonSerializer.Deserialize<VdfRevelationOperation>(rawJson, options),
                "drain_delegate"                => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create DrainOperation   
                "register_constant"             => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create RegisterConstantOperation
                "set_deposits_limit"            => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create SetDepositsLimitOperation
                "migration"                     => JsonSerializer.Deserialize<MigrationOperation>(rawJson, options),
                "revelation_penalty"            => JsonSerializer.Deserialize<RevelationPenaltyOperation>(rawJson, options),
                "baking"                        => JsonSerializer.Deserialize<BakingOperation>(rawJson, options),
                "endorsing_reward"              => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create EndorsmentRewardOperation
                "transfer_ticket"               => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create TransferTicketOperation
                "tx_rollup_commit"              => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create TxRollupCommitOperation
                "tx_rollup_dispatch_tickets"    => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create TxRollupDispatchTicketsOperation
                "tx_rollup_finalize_commitment" => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create TxRollupFinalizeCommitmentOperation
                "tx_rollup_origination"         => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create TxRollupOriginationOperation
                "tx_rollup_rejection"           => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create TxRollupRejectionOperation
                "tx_rollup_remove_commitment"   => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create TxRollupRemoveCommitmentOperation
                "tx_rollup_return_bond"         => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create TxRollupReturnBondOperation
                "tx_rollup_submit_batch"        => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create TxRollupSubmitBatchOperation
                "increase_paid_storage"         => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create IncreasePaidStorageOperation
                "update_consensus_key"          => JsonSerializer.Deserialize<Operation>(rawJson, options), // todo: create UpdateConsensusKeyOperation

                _ => JsonSerializer.Deserialize<Operation>(rawJson, options) // unknown operation
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            Operation? value,
            JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}