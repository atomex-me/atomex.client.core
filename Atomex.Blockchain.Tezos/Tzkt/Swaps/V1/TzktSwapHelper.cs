using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Tezos.Common;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos.Tzkt.Swaps.V1
{
    public static class TzktSwapHelper
    {
        public static async Task<Result<IEnumerable<TezosOperation>>> FindLocksAsync(
            this TzktApi api,
            string secretHash,
            string contractAddress,
            string address,
            ulong timeStamp,
            ulong lockTime,
            BigInteger? payoff = null,
            CancellationToken cancellationToken = default)
        {
            var parameter = $"parameter.settings.hashed_secret={secretHash}" +
                $"&parameter.participant={address}" +
                $"&parameter.settings.refund_time={(timeStamp + lockTime).ToIso8601()}" +
                (payoff != null ? $"&parameter.settings.payoff={payoff.Value}" : "");

            var (ops, error) = await api
                .GetOperationsByAddressAsync(
                    address: contractAddress,
                    timeStamp: new DateTimeParameter(DateTimeExtensions.FromUnixTimeSeconds(timeStamp), EqualityType.Ge),
                    type: "transaction",
                    entrypoint: "initiate",
                    parameter: parameter,
                    michelineFormat: MichelineFormat.Json,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return new Result<IEnumerable<TezosOperation>> { Value = ops };
        }

        public static async Task<Result<IEnumerable<TezosOperation>>> FindFa12LocksAsync(
            this TzktApi api,
            string secretHash,
            string contractAddress,
            string address,
            ulong timeStamp,
            ulong lockTime,
            string tokenContract,
            BigInteger? totalAmount = null,
            BigInteger? payoffAmount = null,
            CancellationToken cancellationToken = default)
        {
            var parameter = $"parameter.hashedSecret={secretHash}" +
                $"&parameter.refundTime={(timeStamp + lockTime).ToIso8601()}" +
                $"&parameter.participant={address}" +
                $"&parameter.tokenAddress={tokenContract}" +
                (totalAmount != null ? $"&parameter.totalAmount.ge={totalAmount.Value}" : "") +
                (payoffAmount != null ? $"&parameter.payoffAmount={payoffAmount.Value}" : "");

            var (ops, error) = await api
                .GetOperationsByAddressAsync(
                    address: contractAddress,
                    timeStamp: new DateTimeParameter(DateTimeExtensions.FromUnixTimeSeconds(timeStamp), EqualityType.Ge),
                    type: "transaction",
                    entrypoint: "initiate",
                    parameter: parameter,
                    michelineFormat: MichelineFormat.Json,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return new Result<IEnumerable<TezosOperation>> { Value = ops };
        }

        public static async Task<Result<IEnumerable<TezosOperation>>> FindFa2LocksAsync(
            this TzktApi api,
            string secretHash,
            string contractAddress,
            string address,
            ulong timeStamp,
            ulong lockTime,
            string tokenContract,
            BigInteger tokenId,
            BigInteger? totalAmount = null,
            BigInteger? payoffAmount = null,
            CancellationToken cancellationToken = default)
        {
            var parameter = $"parameter.hashedSecret={secretHash}" +
                $"&parameter.refundTime={(timeStamp + lockTime).ToIso8601()}" +
                $"&parameter.participant={address}" +
                $"&parameter.tokenAddress={tokenContract}" +
                $"&parameter.tokenId={tokenId}" +
                (totalAmount != null ? $"&parameter.totalAmount.ge={totalAmount.Value}" : "") +
                (payoffAmount != null ? $"&parameter.payoffAmount={payoffAmount.Value}" : "");

            var (ops, error) = await api
                .GetOperationsByAddressAsync(
                    address: contractAddress,
                    timeStamp: new DateTimeParameter(DateTimeExtensions.FromUnixTimeSeconds(timeStamp), EqualityType.Ge),
                    type: "transaction",
                    entrypoint: "initiate",
                    parameter: parameter,
                    michelineFormat: MichelineFormat.Json,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return new Result<IEnumerable<TezosOperation>> { Value = ops };
        }

        public static async Task<Result<IEnumerable<TezosOperation>>> FindAdditionalLocksAsync(
            this TzktApi api,
            string secretHash,
            string contractAddress,
            ulong timeStamp,
            CancellationToken cancellationToken = default)
        {
            var parameter = $"parameter={secretHash}";

            var (ops, error) = await api
                .GetOperationsByAddressAsync(
                    address: contractAddress,
                    timeStamp: new DateTimeParameter(DateTimeExtensions.FromUnixTimeSeconds(timeStamp), EqualityType.Ge),
                    type: "transaction",
                    entrypoint: "add",
                    parameter: parameter,
                    michelineFormat: MichelineFormat.Json,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return new Result<IEnumerable<TezosOperation>> { Value = ops };
        }

        public static async Task<Result<IEnumerable<TezosOperation>>> FindRedeemsAsync(
            this TzktApi api,
            string secretHash,
            string contractAddress,
            ulong timeStamp,
            CancellationToken cancellationToken = default)
        {
            const int redeemTimeOutInSec = 86400; // 24 hours
            var redeemTimeOut = DateTimeExtensions.FromUnixTimeSeconds(timeStamp + redeemTimeOutInSec);

            var (ops, error) = await api
                .GetOperationsByAddressAsync(
                    address: contractAddress,
                    timeStamp: new DateTimeParameter
                    {
                        { DateTimeExtensions.FromUnixTimeSeconds(timeStamp), EqualityType.Ge },
                        { redeemTimeOut, EqualityType.Lt }
                    },
                    type: "transaction",
                    entrypoint: "redeem",
                    michelineFormat: MichelineFormat.Json,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            var redeems = ops
                .Where(o => o.IsRedeem(contractAddress, secretHash, out _))
                .ToList();

            return new Result<IEnumerable<TezosOperation>> { Value = redeems };
        }

        public static async Task<Result<IEnumerable<TezosOperation>>> FindRefundsAsync(
            this TzktApi api,
            string secretHash,
            string contractAddress,
            ulong fromTimeStamp,
            CancellationToken cancellationToken = default)
        {
            var parameter = $"parameter={secretHash}";

            var (ops, error) = await api
                .GetOperationsByAddressAsync(
                    address: contractAddress,
                    timeStamp: new DateTimeParameter(DateTimeExtensions.FromUnixTimeSeconds(fromTimeStamp), EqualityType.Ge),
                    type: "transaction",
                    entrypoint: "refund",
                    parameter: parameter,
                    michelineFormat: MichelineFormat.Json,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return new Result<IEnumerable<TezosOperation>> { Value = ops };
        }
    }
}