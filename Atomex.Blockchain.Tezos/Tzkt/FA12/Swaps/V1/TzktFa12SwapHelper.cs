using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Tezos.Common;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos.Tzkt.Fa12.Swaps.V1
{
    public class TzktFa12SwapHelper
    {
        public static async Task<(IEnumerable<TezosOperation> txs, Error error)> FindLocksAsync(
            TzktApi api,
            string secretHash,
            string contractAddress,
            string address,
            ulong timeStamp,
            ulong lockTime,
            string tokenContract,
            CancellationToken cancellationToken = default)
        {
            var filter = "type=transaction&" +
                "entrypoint=initiate&" +
                $"parameter.hashedSecret={secretHash}&" +
                $"parameter.refundTime.ge={(timeStamp + lockTime).ToIso8601()}&" +
                $"parameter.participant={address}&" +
                $"parameter.tokenAddress={tokenContract}";

            var (ops, error) = await api
                .GetOperationsAsync(
                    address: contractAddress,
                    fromTimeStamp: DateTimeOffset.FromUnixTimeSeconds((long)timeStamp),
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
            var filter = "type=transaction&" +
                "entrypoint=redeem";

            var fromTimeStamp = DateTimeOffset.FromUnixTimeSeconds((long)timeStamp);

            var (ops, error) = await api
                .GetOperationsAsync(
                    address: contractAddress,
                    fromTimeStamp: fromTimeStamp,
                    filter: filter,
                    michelineFormat: MichelineFormat.Json,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            //var redeems = ops.Where(o => IsRedeem(o, contractAddress, secretHash));

            return (txs: ops, error: null);
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

            var fromTimeStamp = DateTimeOffset.FromUnixTimeSeconds((long)timeStamp);

            var (ops, error) = await api
                .GetOperationsAsync(
                    address: contractAddress,
                    fromTimeStamp: fromTimeStamp,
                    filter: filter,
                    michelineFormat: MichelineFormat.RawMichelineString,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs: ops, error: null);
        }
    }
}