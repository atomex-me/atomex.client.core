using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Common;
using Atomex.Wallets.Abstract;
using Atomex.Wallets.Common;

namespace Atomex.Wallets.Ethereum
{
    public class EthereumWalletScanner : WalletScanner<IEthereumApi>
    {
        public List<(string address, DateTimeOffset lastUpdateTime)> ChangedAddresses { get; } =
            new List<(string address, DateTimeOffset lastUpdateTime)>();

        private EthereumAccount Account => _account as EthereumAccount;

        public EthereumWalletScanner(
            EthereumAccount account,
            IWalletProvider walletProvider,
            ILogger logger = null)
            : base(account, walletProvider, logger)
        {
        }

        public Task<(IEnumerable<EthereumTransaction> txs, Error error)> GetTransactionsAsync(
            IEnumerable<(string address, DateTimeOffset lastUpdateTime)> addresses,
            CancellationToken cancellationToken = default)
        {
            return Task.Run<(IEnumerable<EthereumTransaction> txs, Error error)>(async () =>
            {
                var api = GetBlockchainApi();

                var txs = new List<EthereumTransaction>();

                foreach (var (address, lastUpdateTime) in addresses)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var (addressTxs, error) = await api
                        .GetTransactionsAsync(address, lastUpdateTime, cancellationToken)
                        .ConfigureAwait(false);

                    txs.AddRange(addressTxs);
                }

                return (txs, error: null);

            }, cancellationToken);
        }

        public Task<Error> ScanTransactionsAsync(
            IEnumerable<(string address, DateTimeOffset lastUpdateTime)> addresses,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var (txs, error) = await GetTransactionsAsync(
                        addresses: addresses,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;

                var upserted = await Account
                    .DataRepository
                    .UpsertTransactionsAsync(txs, cancellationToken)
                    .ConfigureAwait(false);

                return null;

            }, cancellationToken);
        }

        public async Task<Error> ScanTransactionsAsync(
            IEnumerable<string> addresses,
            CancellationToken cancellationToken = default)
        {
            return await ScanTransactionsAsync(
                    addresses: addresses.Select(a => (address: a, lastUpdateTime: DateTimeOffset.MinValue)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        protected override async Task<(bool hasActivity, Error error)> UpdateAddressBalanceAsync(
            string address,
            string keyPath,
            WalletInfo walletInfo,
            WalletAddress storedAddress,
            IEthereumApi api,
            CancellationToken cancellationToken = default)
        {
            var (balance, error) = await api
                .GetBalanceAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                _logger.LogError("[{currency}] Error while get balance for {address}", Account.Currency, address);
                return (hasActivity: false, error);
            }

            var (txsCount, txsCountError) = await api
                .GetTransactionsCountAsync(
                    address,
                    pending: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsCountError != null)
            {
                _logger.LogError("[{currency}] Error while get transactions count for {address}", Account.Currency, address);
                return (hasActivity: false, error: txsCountError);
            }

            var counter = txsCount.GetValueOrDefault(0);

            if (storedAddress == null ||
                storedAddress.Balance.Total != balance ||
                storedAddress.Counter != counter) // todo: control new input transactions without balance changes
            {
                // save changed address to be able to scan transactions later
                ChangedAddresses.Add((address, storedAddress?.Balance.LastUpdateTime ?? DateTimeOffset.MinValue));
            }

            var hasActivity = (storedAddress != null && (storedAddress.HasActivity || storedAddress.Balance.Total > 0 || storedAddress.Counter > 0))
                || balance > 0
                || counter > 0; // todo: fix for zero balances account with input transactions only

            var updatedAddress = new WalletAddress
            {
                Currency    = Account.Currency,
                Address     = address,
                Balance     = new Balance(balance, DateTimeOffset.UtcNow),
                WalletId    = walletInfo.Id,
                KeyPath     = keyPath,
                KeyIndex    = !walletInfo.IsSingleKeyWallet
                    ? keyPath.GetIndex(walletInfo.KeyPathPattern, KeyPathExtensions.IndexPattern)
                    : 0,
                HasActivity = hasActivity,
                Counter     = counter
            };

            await Account
                .DataRepository
                .UpsertAddressAsync(updatedAddress, cancellationToken)
                .ConfigureAwait(false);

            return (hasActivity, error: null); // no errors
        }

        protected override CurrencyConfig GetCurrencyConfig() =>
            Account.Configuration;

        protected override IEthereumApi GetBlockchainApi() => new EthereumApi(
            settings: Account.Configuration.ApiSettings,
            logger: _logger);
    }
}