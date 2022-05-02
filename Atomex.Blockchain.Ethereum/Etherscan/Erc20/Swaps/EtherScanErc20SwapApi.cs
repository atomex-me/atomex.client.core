using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum.Etherscan.Erc20.Swaps.V1;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum.Etherscan.Erc20.Swaps
{
    public class EtherScanErc20SwapApi : IBlockchainSwapApi
    {
        protected readonly string _token;
        private readonly EtherScanApi _etherScanApi;

        public EtherScanErc20SwapApi(string token, EtherScanSettings settings)
            : this(token, new EtherScanApi(settings))
        {
        }

        public EtherScanErc20SwapApi(string token, EtherScanApi etherScanApi)
        {
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _etherScanApi = etherScanApi ?? throw new ArgumentNullException(nameof(etherScanApi));
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
            var (txs, error) = await EtherScanErc20SwapHelper
                .FindLocksAsync(
                    api: _etherScanApi,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    contractBlock: _etherScanApi.Settings.GetBlock(contractAddress),
                    address: address,
                    timeStamp: timeStamp,
                    lockTime: lockTime,
                    tokenContract: _etherScanApi.Settings.GetTokenContract(_token),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs, error: null);
        }

        public async Task<(IEnumerable<Transaction> txs, Error error)> FindAdditionalLocksAsync(
            string secretHash,
            string contractAddress,
            ulong timeStamp,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            var (txs, error) = await EtherScanErc20SwapHelper
                .FindAdditionalLocksAsync(
                    api: _etherScanApi,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    contractBlock: _etherScanApi.Settings.GetBlock(contractAddress),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs, error: null);
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
            var (txs, error) = await EtherScanErc20SwapHelper
                .FindRedeemsAsync(
                    api: _etherScanApi,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    contractBlock: _etherScanApi.Settings.GetBlock(contractAddress),
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
            var (txs, error) = await EtherScanErc20SwapHelper
                .FindRefundsAsync(
                    api: _etherScanApi,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    contractBlock: _etherScanApi.Settings.GetBlock(contractAddress),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs, error: null);
        }

        #endregion IBlockchainSwapApi
    }
}