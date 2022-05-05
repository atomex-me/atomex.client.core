using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Abstract;
using Atomex.Common;
using Atomex.Wallets.Abstract;
using Atomex.Wallets.Common;

namespace Atomex.Wallets.Tezos
{
    public class TezosWalletScanner : WalletScanner<ITezosApi>
    {
        public List<(string address, DateTimeOffset lastUpdateTime)> ChangedAddresses { get; } =
            new List<(string address, DateTimeOffset lastUpdateTime)>();

        private TezosAccount Account => _account as TezosAccount;

        public TezosWalletScanner(
            TezosAccount account,
            IWalletProvider walletProvider,
            ILogger logger = null)
            : base(account, walletProvider, logger)
        {
        }

        public Task<(IEnumerable<TezosOperation> ops, Error error)> GetOperationsAsync(
            IEnumerable<(string address, DateTimeOffset lastUpdateTime)> addresses,
            CancellationToken cancellationToken = default)
        {
            return Task.Run<(IEnumerable<TezosOperation> ops, Error error)>(async () =>
            {
                var api = GetBlockchainApi();

                var ops = new List<TezosOperation>();

                foreach (var (address, lastUpdateTime) in addresses)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var (addressOps, error) = await api
                        .GetOperationsAsync(address, lastUpdateTime, cancellationToken)
                        .ConfigureAwait(false);

                    ops.AddRange(addressOps);
                }

                return (ops, error: null);

            }, cancellationToken);
        }

        public Task<Error> ScanOperationsAsync(
            IEnumerable<(string address, DateTimeOffset lastUpdateTime)> addresses,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var (ops, error) = await GetOperationsAsync(
                        addresses: addresses,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;

                var upserted = await Account
                    .UpsertTransactionsAsync(ops, cancellationToken)
                    .ConfigureAwait(false);

                return null;

            }, cancellationToken);
        }

        public async Task<Error> ScanOperationsAsync(
            IEnumerable<string> addresses,
            CancellationToken cancellationToken = default)
        {
            return await ScanOperationsAsync(
                    addresses: addresses.Select(a => (address: a, lastUpdateTime: DateTimeOffset.MinValue)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        protected override async Task<(bool hasActivity, Error error)> UpdateAddressBalanceAsync(
            string address,
            string keyPath,
            WalletInfo walletInfo,
            WalletAddress storedAddress,
            ITezosApi api = default,
            CancellationToken cancellationToken = default)
        {
            var (account, error) = await api
                .GetAccountAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                _logger.LogError("[{currency}] Error while get account info for {address}", Account.Currency, address);
                return (hasActivity: false, error);
            }

            if (account == null)
            {
                _logger.LogError("[{currency}] Null account info received for {address}", Account.Currency, address);
                return (hasActivity: false, error: new Error(Errors.GetAccountError, $"Null account info received for {address}"));
            }

            if (storedAddress == null ||
                storedAddress.Balance.Total != account.Balance.ToTez() ||
                storedAddress.Counter != account.Counter ||
                storedAddress.Balance.LastUpdateTime < account.LastActivityTime)
            {
                // save changed address to be able to scan transactions later
                ChangedAddresses.Add((address, storedAddress?.Balance.LastUpdateTime ?? DateTimeOffset.MinValue));
            }

            await Account
                .UpsertAddressAsync(new WalletAddress
                {
                    Currency = Account.Currency,
                    Address  = address,
                    Balance  = new Balance(account.Balance.ToTez(), DateTimeOffset.UtcNow),
                    WalletId = walletInfo.Id,
                    KeyPath  = keyPath,
                    KeyIndex = !walletInfo.IsSingleKeyWallet
                        ? keyPath.GetIndex(walletInfo.KeyPathPattern, KeyPathExtensions.IndexPattern)
                        : 0,
                    HasActivity = account.HasActivity,
                    Counter     = account.Counter
                }, cancellationToken)
                .ConfigureAwait(false);

            return (account.HasActivity, error: null); // no errors
        }

        protected override CurrencyConfig GetCurrencyConfig() => Account.Configuration;

        protected override ITezosApi GetBlockchainApi() => new TezosApi(
            settings: Account.Configuration.ApiSettings,
            logger: _logger);
    }
}