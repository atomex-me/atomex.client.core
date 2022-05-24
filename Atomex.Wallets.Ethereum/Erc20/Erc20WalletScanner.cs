using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Blockchain.Ethereum.Erc20;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Wallets.Abstract;
using Atomex.Wallets.Common;

namespace Atomex.Wallets.Ethereum.Erc20
{
    public class Erc20WalletScanner : WalletScanner<IErc20Api>
    {
        public List<(string address, DateTimeOffset lastUpdateTime)> ChangedAddresses { get; } =
            new List<(string address, DateTimeOffset lastUpdateTime)>();

        private Erc20Account Account => _account as Erc20Account;

        public Erc20WalletScanner(
            Erc20Account account,
            IWalletProvider walletProvider,
            ILogger logger = null)
            : base(account, walletProvider, logger)
        {
        }

        protected override string AddressFromKey(
            SecureBytes publicKey,
            WalletInfo walletInfo = null) =>
            Account.Configuration.AddressFromKey(publicKey, walletInfo);

        protected override IErc20Api GetBlockchainApi() => new Erc20Api(
            settings: Account.Configuration.ApiSettings,
            logger: _logger);

        protected override async Task<(bool hasActivity, Error error)> UpdateAddressBalanceAsync(
            string address,
            string keyPath,
            WalletInfo walletInfo,
            WalletAddress storedAddress,
            IErc20Api api,
            CancellationToken cancellationToken = default)
        {
            var (balanceInUnits, error) = await api
                .GetErc20BalanceAsync(address, token: Account.Currency, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                _logger.LogError("[{currency}] Error while get balance for {address}", Account.Currency, address);
                return (hasActivity: false, error);
            }

            var balance = EthereumHelper.BaseTokenUnitsToTokens(
                tokenUnits: balanceInUnits,
                decimalsMultiplier: Account.Configuration.DecimalsMultiplier);

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
    }
}