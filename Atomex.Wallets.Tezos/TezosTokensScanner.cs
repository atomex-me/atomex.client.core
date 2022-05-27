using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallets.Tezos
{
    public class TezosTokensScanner : IWalletScanner
    {
        private readonly TezosAccount _account;

        public TezosTokensScanner(TezosAccount account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public Task<Error> UpdateAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public async Task<Error> UpdateBalanceAsync(
            bool forceUpdate = false,
            CancellationToken cancellationToken = default)
        {
            var wallets = await _account
                .DataRepository
                .GetWalletsInfoAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var wallet in wallets)
            {
                var error = await UpdateBalanceAsync(
                        walletId: wallet.Id,
                        forceUpdate: forceUpdate,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;
            }

            return null;
        }

        public Task<Error> UpdateBalanceAsync(
            int walletId,
            bool forceUpdate = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Error> UpdateAddressBalanceAsync(
            string address,
            string tokenContract,
            int tokenId = 0,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<Error> ScanAsync(
            bool forceUpdate = false,
            CancellationToken cancellationToken = default)
        {
            var wallets = await _account
                .DataRepository
                .GetWalletsInfoAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var wallet in wallets)
            {
                var error = await ScanAsync(
                        walletId: wallet.Id,
                        forceUpdate: forceUpdate,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;
            }

            return null;
        }

        public Task<Error> ScanAsync(
            int walletId,
            bool forceUpdate = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}