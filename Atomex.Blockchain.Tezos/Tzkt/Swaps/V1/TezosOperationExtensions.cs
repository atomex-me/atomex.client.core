using System;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Atomex.Blockchain.Tezos.Common;
using Atomex.Blockchain.Tezos.Tzkt.Operations;
using Atomex.Common;
using Atomex.Cryptography.Abstract;

namespace Atomex.Blockchain.Tezos.Tzkt.Swaps.V1
{
    public static class TezosOperationExtensions
    {
        public class InitiateSettings
        {
            [JsonPropertyName("payoff")]
            public string Payoff { get; set; }
            [JsonPropertyName("refund_time")]
            public string RefundTime { get; set; }
            [JsonPropertyName("hashed_secret")]
            public string HashedSecret { get; set; }
        }

        public class InitiateParameters
        {
            [JsonPropertyName("settings")]
            public InitiateSettings Settings { get; set; }
            [JsonPropertyName("participant")]
            public string Participant { get; set; }
        }

        public class Fa12InitiateParameters
        {
            [JsonPropertyName("hashedSecret")]
            public string HashedSecret { get; set; }
            [JsonPropertyName("participant")]
            public string Participant { get; set; }
            [JsonPropertyName("payoffAmount")]
            public string PayoffAmount { get; set; }
            [JsonPropertyName("refundTime")]
            public string RefundTime { get; set; }
            [JsonPropertyName("tokenAddress")]
            public string TokenAddress { get; set; }
            [JsonPropertyName("totalAmount")]
            public string TotalAmount { get; set; }
        }

        public class Fa2InitiateParameters
        {
            [JsonPropertyName("hashedSecret")]
            public string HashedSecret { get; set; }
            [JsonPropertyName("participant")]
            public string Participant { get; set; }
            [JsonPropertyName("payoffAmount")]
            public string PayoffAmount { get; set; }
            [JsonPropertyName("refundTime")]
            public string RefundTime { get; set; }
            [JsonPropertyName("tokenAddress")]
            public string TokenAddress { get; set; }
            [JsonPropertyName("tokenId")]
            public string TokenId { get; set; }
            [JsonPropertyName("totalAmount")]
            public string TotalAmount { get; set; }
        }

        public static bool TryFindInitiate(
            this TezosOperation operation,
            string contractAddress,
            string secretHash,
            long refundTime,
            string participant,
            BigInteger? payoff,
            out TransactionOperation? initiateTx)
        {
            initiateTx = null;

            foreach (var op in operation.Operations)
            {
                if (op is not TransactionOperation tx)
                    continue;

                if (tx.Target?.Address != contractAddress)
                    continue;

                if (tx.Parameter.Entrypoint != "initiate")
                    continue;

                try
                {
                    var parameters = DeserializeObject<InitiateParameters>(tx.Parameter, operation.ParametersFormat);

                    if (parameters == null)
                        continue;

                    if (secretHash != parameters.Settings.HashedSecret)
                        continue;

                    if (refundTime != DateTime.Parse(parameters.Settings.RefundTime).ToUnixTimeSeconds())
                        continue;

                    if (participant != parameters.Participant)
                        continue;

                    if (payoff != null && payoff != BigInteger.Parse(parameters.Settings.Payoff))
                        continue;
                }
                catch
                {
                    continue;
                }

                initiateTx = tx;

                return true;
            }

            return false;
        }

        public static bool TryFindFa12Initiate(
            this TezosOperation operation,
            string contractAddress,
            string secretHash,
            long refundTime,
            string participant,
            string tokenContract,
            BigInteger? totalAmount,
            BigInteger? payoffAmount,
            out TransactionOperation? initiateTx)
        {
            initiateTx = null;

            foreach (var op in operation.Operations)
            {
                if (op is not TransactionOperation tx)
                    continue;

                if (tx.Target?.Address != contractAddress)
                    continue;

                if (tx.Parameter.Entrypoint != "initiate")
                    continue;

                try
                {
                    var parameters = DeserializeObject<Fa12InitiateParameters>(tx.Parameter, operation.ParametersFormat);

                    if (parameters == null)
                        continue;

                    if (secretHash != parameters.HashedSecret)
                        continue;

                    if (refundTime != DateTime.Parse(parameters.RefundTime).ToUnixTimeSeconds())
                        continue;

                    if (participant != parameters.Participant)
                        continue;

                    if (tokenContract != parameters.TokenAddress)
                        continue;

                    if (totalAmount != null && totalAmount > BigInteger.Parse(parameters.TotalAmount))
                        continue;

                    if (payoffAmount != null && payoffAmount != BigInteger.Parse(parameters.PayoffAmount))
                        continue;
                }
                catch
                {
                    continue;
                }

                initiateTx = tx;

                return true;
            }

            return false;
        }

        public static bool TryFindFa2Initiate(
            this TezosOperation operation,
            string contractAddress,
            string secretHash,
            long refundTime,
            string participant,
            string tokenContract,
            BigInteger tokenId,
            BigInteger? totalAmount,
            BigInteger? payoffAmount,
            out TransactionOperation? initiateTx)
        {
            initiateTx = null;

            foreach (var op in operation.Operations)
            {
                if (op is not TransactionOperation tx)
                    continue;

                if (tx.Target?.Address != contractAddress)
                    continue;

                if (tx.Parameter.Entrypoint != "initiate")
                    continue;

                try
                {
                    var parameters = DeserializeObject<Fa2InitiateParameters>(tx.Parameter, operation.ParametersFormat);

                    if (parameters == null)
                        continue;

                    if (secretHash != parameters.HashedSecret)
                        continue;

                    if (refundTime != DateTime.Parse(parameters.RefundTime).ToUnixTimeSeconds())
                        continue;

                    if (participant != parameters.Participant)
                        continue;

                    if (tokenContract != parameters.TokenAddress)
                        continue;

                    if (tokenId != BigInteger.Parse(parameters.TokenId))
                        continue;

                    if (totalAmount != null && totalAmount > BigInteger.Parse(parameters.TotalAmount))
                        continue;

                    if (payoffAmount != null && payoffAmount != BigInteger.Parse(parameters.PayoffAmount))
                        continue;
                }
                catch
                {
                    continue;
                }

                initiateTx = tx;

                return true;
            }

            return false;
        }

        public static bool TryFindAdd(
            this TezosOperation operation,
            string contractAddress,
            string secretHash,
            out TransactionOperation? addTx)
        {
            addTx = null;

            foreach (var op in operation.Operations)
            {
                if (op is not TransactionOperation tx)
                    continue;

                if (tx.Target?.Address != contractAddress)
                    continue;

                if (tx.Parameter.Entrypoint != "add")
                    continue;

                try
                {
                    var secretHashHex = DeserializeString(tx.Parameter, operation.ParametersFormat);

                    if (secretHashHex != secretHash)
                        continue;
                }
                catch
                {
                    continue;
                }

                addTx = tx;

                return true;
            }

            return false;
        }

        public static bool IsRefund(
            this TezosOperation operation,
            string contractAddress,
            string secretHash)
        {
            foreach (var op in operation.Operations)
            {
                if (op is not TransactionOperation tx)
                    continue;

                if (tx.Target?.Address != contractAddress)
                    continue;

                if (tx.Parameter.Entrypoint != "refund")
                    continue;

                try
                {
                    var secretHashHex = DeserializeString(tx.Parameter, operation.ParametersFormat);

                    if (secretHashHex != secretHash)
                        continue;
                }
                catch
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        public static bool IsRedeem(
            this TezosOperation operation,
            string contractAddress,
            string secretHash,
            out string? secret)
        {
            secret = null;

            foreach (var op in operation.Operations)
            {
                if (op is not TransactionOperation tx)
                    continue;

                if (tx.Target?.Address != contractAddress)
                    continue;

                if (tx.Parameter.Entrypoint != "redeem")
                    continue;

                var secretHex = DeserializeString(tx.Parameter, operation.ParametersFormat);

                if (secretHex == null)
                    continue;

                var secretBytes = Hex.FromString(secretHex);
                var secretHashBytes = Hex.FromString(secretHash);

                var computedSecretHash = HashAlgorithm.Sha256.Hash(HashAlgorithm.Sha256.Hash(secretBytes));

                if (!computedSecretHash.SequenceEqual(secretHashBytes))
                    continue;

                secret = secretHex;

                return true;
            }

            return false;
        }

        public static T? DeserializeObject<T>(
            this Parameter parameter,
            MichelineFormat format)
        {
            if (format == MichelineFormat.Json)
            {
                return parameter.Value.ToObject<T>();
            }
            else if (format == MichelineFormat.JsonString)
            {
                return JsonSerializer.Deserialize<T>(parameter.Value.GetRawText());
            }
            else
            {
                throw new NotImplementedException("Michelson processing removed in favor of parsed json parameters");
            }
        }

        public static string? DeserializeString(
            this Parameter parameter,
            MichelineFormat format)
        {
            if (format == MichelineFormat.Json)
            {
                return parameter.Value.GetString();
            }
            else if (format == MichelineFormat.JsonString)
            {
                return parameter.Value.GetString()?.Trim('"');
            }
            else
            {
                throw new NotImplementedException("Michelson processing removed in favor of parsed json parameters");
            }
        }
    }
}