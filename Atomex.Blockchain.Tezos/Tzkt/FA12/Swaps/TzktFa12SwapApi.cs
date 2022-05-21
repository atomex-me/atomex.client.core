using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Tzkt.Fa12.Swaps.V1;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos.Tzkt.Fa12.Swaps
{
    public class TzktFa12SwapApi : IBlockchainSwapApi
    {
        protected readonly string _token;
        private readonly TzktApi _tzktApi;

        public TzktFa12SwapApi(string token, TzktSettings settings)
            : this(token, new TzktApi(settings))
        {
        }

        public TzktFa12SwapApi(string token, TzktApi tzktApi)
        {
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _tzktApi = tzktApi ?? throw new ArgumentNullException(nameof(tzktApi));
        }

        #region IBlockchainSwapApi

        public async Task<(IEnumerable<Transaction> txs, Error error)> FindLocksAsync(
            string secretHash,
            string contractAddress,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            var (txs, error) = await TzktFa12SwapHelper
                .FindLocksAsync(
                    api: _tzktApi,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    address: address,
                    timeStamp: timeStamp,
                    lockTime: lockTime,
                    tokenContract: _tzktApi.Settings.GetTokenContract(_token),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs, error: null);
        }

        public Task<(IEnumerable<Transaction> txs, Error error)> FindAdditionalLocksAsync(
            string secretHash,
            string contractAddress,
            ulong timeStamp,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            // notice: method "add" was removed from FA12 swap contract
            return Task.FromResult((txs: Enumerable.Empty<Transaction>(), error: (Error)null));
        }

        public async Task<(IEnumerable<Transaction> txs, Error error)> FindRedeemsAsync(
            string secretHash,
            string contractAddress,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            var (txs, error) = await TzktFa12SwapHelper
                .FindRedeemsAsync(
                    api: _tzktApi,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    timeStamp: timeStamp,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs, error: null);
        }

        public async Task<(IEnumerable<Transaction> txs, Error error)> FindRefundsAsync(
            string secretHash,
            string contractAddress,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            var (txs, error) = await TzktFa12SwapHelper
                .FindRefundsAsync(
                    api: _tzktApi,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    timeStamp: timeStamp,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs, error: null);
        }

        #endregion IBlockchainSwapApi
    }
}