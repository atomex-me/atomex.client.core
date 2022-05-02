using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Tezos.Common;
using Atomex.Blockchain.Tezos.Operations;
using Atomex.Common;
using Atomex.Cryptography.Abstract;

namespace Atomex.Blockchain.Tezos.Tzkt.Swaps.V1
{
    public class TzktSwapHelper
    {
        public static async Task<(IEnumerable<TezosOperation> txs, Error error)> FindLocksAsync(
            TzktApi api,
            string secretHash,
            string contractAddress,
            string address,
            ulong timeStamp,
            ulong lockTime,
            CancellationToken cancellationToken = default)
        {
            var filter = "type=transaction&" +
                "entrypoint=initiate&" +
                $"parameter.settings.hashed_secret={secretHash}&" +
                $"parameter.settings.refund_time.ge={(timeStamp + lockTime).ToIso8601()}&" +
                $"parameter.participant={address}";

            var (ops, error) = await api
                .GetOperationsAsync(
                    address: contractAddress,
                    fromTimeStamp: DateTimeExtensions.FromUnixTimeSeconds(timeStamp),
                    filter: filter,
                    michelineFormat: MichelineFormat.RawMichelineString,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs: ops, error: null);
        }

        public static async Task<(IEnumerable<TezosOperation> txs, Error error)> FindAdditionalLocksAsync(
            TzktApi api,
            string secretHash,
            string contractAddress,
            ulong timeStamp,
            CancellationToken cancellationToken = default)
        {
            var filter = "type=transaction&" +
                "entrypoint=add&" +
                $"parameter={secretHash}";

            var (ops, error) = await api
                .GetOperationsAsync(
                    address: contractAddress,
                    fromTimeStamp: DateTimeExtensions.FromUnixTimeSeconds(timeStamp),
                    filter: filter,
                    michelineFormat: MichelineFormat.RawMichelineString,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs: ops, error: null);
        }

        public static async Task<(IEnumerable<TezosOperation> txs, Error error)> FindRedeemsAsync(
            TzktApi api,
            string secretHash,
            string contractAddress,
            ulong timeStamp,
            CancellationToken cancellationToken = default)
        {
            const int redeemTimeOutInSec = 86400; // 24 hours
            var redeemTimeOut = DateTimeExtensions.FromUnixTimeSeconds(timeStamp + redeemTimeOutInSec);

            var filter = "type=transaction&" +
                "entrypoint=redeem&" +
                $"timestamp.lt={redeemTimeOut.ToIso8601()}";

            var (ops, error) = await api
                .GetOperationsAsync(
                    address: contractAddress,
                    fromTimeStamp: DateTimeExtensions.FromUnixTimeSeconds(timeStamp),
                    filter: filter,
                    michelineFormat: MichelineFormat.Json,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            var redeems = ops
                .Where(o => IsRedeem(o, contractAddress, secretHash))
                .ToList();

            return (txs: redeems, error: null);
        }

        public static async Task<(IEnumerable<TezosOperation> txs, Error error)> FindRefundsAsync(
            TzktApi api,
            string secretHash,
            string contractAddress,
            ulong timeStamp,
            CancellationToken cancellationToken = default)
        {
            var filter = "type=transaction&" +
                "entrypoint=refund&" +
                $"parameter={secretHash}";

            var (ops, error) = await api
                .GetOperationsAsync(
                    address: contractAddress,
                    fromTimeStamp: DateTimeExtensions.FromUnixTimeSeconds(timeStamp),
                    filter: filter,
                    michelineFormat: MichelineFormat.RawMichelineString,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs: ops, error: null);
        }

        public static bool IsRedeem(
            TezosOperation operation,
            string contractAddress,
            string secretHash)
        {
            foreach (var op in operation.Operations)
            {
                if (op is not TransactionOperation tx)
                    continue;

                if (tx.Target?.Address != contractAddress)
                    continue;

                if (tx.Parameter.Entrypoint != "redeem")
                    continue;

                if (tx.Parameter.Value is not JsonElement value)
                    continue;

                if (value.GetString() == null)
                    continue;

                var secretBytes = Hex.FromString(value.GetString());
                var secretHashBytes = Hex.FromString(secretHash);

                var computedSecretHash = HashAlgorithm.Sha256.Hash(HashAlgorithm.Sha256.Hash(secretBytes));

                if (!computedSecretHash.SequenceEqual(secretHashBytes))
                    continue;

                return true;
            }

            return false;
        }
    }
}