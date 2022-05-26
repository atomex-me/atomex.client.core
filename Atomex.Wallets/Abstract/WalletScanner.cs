using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Wallets.Common;
using Atomex.Wallets.Bips;

namespace Atomex.Wallets.Abstract
{
    public abstract class WalletScanner<TBlockchainApi> : IWalletScanner
    {
        protected readonly IAccount _account;
        protected readonly IWalletProvider _walletProvider;
        protected readonly ILogger _logger;

        public int LookAhead { get; set; } = 1;

        protected WalletScanner(
            IAccount account,
            IWalletProvider walletFactory,
            ILogger logger = null)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _walletProvider = walletFactory ?? throw new ArgumentNullException(nameof(walletFactory));
            _logger = logger;
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

        /// <inheritdoc/>
        public Task<Error> UpdateBalanceAsync(
            int walletId,
            bool forceUpdate = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var walletInfo = await _account
                    .DataRepository
                    .GetWalletInfoByIdAsync(walletId, cancellationToken)
                    .ConfigureAwait(false);

                if (walletInfo == null)
                    return new Error(
                        code: Errors.WalletNotFoundError,
                        description: $"[{_account.Currency}] Wallet with id {walletId} not found");

                _logger.LogInformation("[{currency}] Update balance for wallet [{wallet}]",
                    _account.Currency,
                    $"{walletId};{walletInfo.Name};{walletInfo.Type}");

                var addresses = await _account
                    .DataRepository
                    .GetAddressesAsync(
                        currency: _account.Currency,
                        walletId: walletId,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var api = GetBlockchainApi();

                foreach (var address in addresses)
                {
                    var (_, error) = await UpdateAddressBalanceAsync(
                            address: address.Address,
                            keyPath: address.KeyPath,
                            walletInfo: walletInfo,
                            api: api,
                            storedAddress: address,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                        return error;
                }

                return null; // no errors
            });
        }

        /// <inheritdoc/>
        public virtual Task<Error> UpdateAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var walletAddress = await _account
                    .DataRepository
                    .GetWalletAddressAsync(_account.Currency, address, cancellationToken)
                    .ConfigureAwait(false);

                if (walletAddress == null)
                    return new Error(
                        code: Errors.WalletNotFoundError,
                        description: $"[{_account.Currency}] Wallet address with {address} not found");

                var walletInfo = await _account
                    .DataRepository
                    .GetWalletInfoByIdAsync(walletAddress.WalletId, cancellationToken)
                    .ConfigureAwait(false);

                if (walletInfo == null)
                    return new Error(
                        code: Errors.WalletNotFoundError,
                        description: $"[{_account.Currency}] Wallet with id {walletAddress.WalletId} not found");

                var (_, error) = await UpdateAddressBalanceAsync(
                        address: address,
                        keyPath: walletAddress.KeyPath,
                        walletInfo: walletInfo,
                        api: GetBlockchainApi(),
                        storedAddress: walletAddress,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return error;
            });
        }

        /// <inheritdoc/>
        public virtual async Task<Error> ScanAsync(
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

        /// <inheritdoc/>
        public virtual Task<Error> ScanAsync(
            int walletId,
            bool forceUpdate = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var walletInfo = await _account
                    .DataRepository
                    .GetWalletInfoByIdAsync(walletId, cancellationToken)
                    .ConfigureAwait(false);

                if (walletInfo == null)
                    return new Error(
                        code: Errors.WalletNotFoundError,
                        description: $"[{_account.Currency}] Wallet with id {walletId} not found");

                var wallet = _walletProvider.GetWallet(walletInfo);

                if (wallet == null)
                    return new Error(
                        code: Errors.WalletError,
                        description: $"[{_account.Currency}] Error creation wallet [{walletId};{walletInfo.Name};{walletInfo.Type}]");

                _logger.LogInformation("[{currency}] Scan balances for wallet [{wallet}]",
                    _account.Currency,
                    $"{walletId};{walletInfo.Name};{walletInfo.Type}");

                var api = GetBlockchainApi();

                return walletInfo.IsSingleKeyWallet
                    ? await UpdateBalanceByKeyPathAsync(
                            keyPath: Wallet.SingleKeyPath,
                            walletInfo: walletInfo,
                            wallet: wallet,
                            api: api,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false)
                    : await ScanHdWalletAsync(
                            walletInfo: walletInfo,
                            wallet: wallet,
                            api: api,
                            forceUpdate: forceUpdate,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
            });
        }

        protected Task<Error> UpdateBalanceByKeyPathAsync(
            string keyPath,
            WalletInfo walletInfo,
            IWallet wallet,
            TBlockchainApi api,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                using var publicKey = await wallet
                    .GetPublicKeyAsync(keyPath, cancellationToken)
                    .ConfigureAwait(false);

                var address = AddressFromKey(publicKey, walletInfo);

                _logger.LogInformation("[{currency}] Scan balance for {address}",
                    _account.Currency,
                    address);

                var storedAddress = await _account
                    .DataRepository
                    .GetWalletAddressAsync(_account.Currency, address, cancellationToken)
                    .ConfigureAwait(false);

                var (_, error) = await UpdateAddressBalanceAsync(
                        address: address,
                        keyPath: keyPath,
                        walletInfo: walletInfo,
                        storedAddress: storedAddress,
                        api: api,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return error;

            }, cancellationToken);
        }

        protected virtual async Task<Error> ScanHdWalletAsync(
            WalletInfo walletInfo,
            IWallet wallet,
            TBlockchainApi api,
            bool forceUpdate = false,
            CancellationToken cancellationToken = default)
        {
            var storedAddresses = (await _account
                .DataRepository
                .GetAddressesAsync(
                    currency: _account.Currency,
                    walletId: walletInfo.Id,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .ToDictionary(a => a.Address, a => a);

            var isMultiChain = walletInfo
                .KeyPathPattern
                .Contains(KeyPathExtensions.ChainPattern);

            var chains = isMultiChain
                ? new int[] { Bip44.External, Bip44.Internal }
                : new int[] { Bip44.External };

            foreach (var chain in chains)
            {
                var freeKeysCount = 0;
                var index = 0u;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var keyPath = walletInfo.KeyPathPattern
                        .SetIndex(walletInfo.KeyPathPattern, KeyPathExtensions.ChainPattern, chain.ToString())
                        .SetIndex(walletInfo.KeyPathPattern, KeyPathExtensions.IndexPattern, index.ToString());

                    using var publicKey = await wallet
                        .GetPublicKeyAsync(keyPath, cancellationToken)
                        .ConfigureAwait(false);

                    var address = AddressFromKey(publicKey, walletInfo);

                    // check if the address is in the stored addresses
                    if (storedAddresses.TryGetValue(address, out var storedAddress))
                    {
                        var canBeSkipped = storedAddress.UsageType == WalletAddressUsageType.NoLongerUsed ||
                            (storedAddress.UsageType == WalletAddressUsageType.Disposable &&
                            storedAddress.HasActivity &&
                            storedAddress.Balance.IsZero());

                        if (!forceUpdate && canBeSkipped)
                        {
                            if (storedAddress.HasActivity)
                                freeKeysCount = 0; 
                            
                            index++;
                            continue;
                        }
                    }

                    var (hasActivity, error) = await UpdateAddressBalanceAsync(
                            address: address,
                            keyPath: keyPath,
                            walletInfo: walletInfo,
                            api: api,
                            storedAddress: storedAddress,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    freeKeysCount = hasActivity  // if has activity
                        ? 0                      // reset counter
                        : freeKeysCount + 1;     // otherwise increment                    

                    if (freeKeysCount >= LookAhead)
                    {
                        _logger.LogInformation("[{currency}] {count} free addresses found. Chain {chain} scan completed",
                            _account.Currency,
                            freeKeysCount,
                            chain);

                        break;
                    }

                    index++; // increment key path index
                }
            }

            return null;
        }

        protected abstract Task<(bool hasActivity, Error error)> UpdateAddressBalanceAsync(
            string address,
            string keyPath,
            WalletInfo walletInfo,
            WalletAddress storedAddress,
            TBlockchainApi api,
            CancellationToken cancellationToken = default);

        protected abstract string AddressFromKey(
            SecureBytes publicKey,
            WalletInfo walletInfo = null);

        protected abstract TBlockchainApi GetBlockchainApi();
    }
}