#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Common;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Common;
using Atomex.Wallets.Bips;
using Atomex.Wallets;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallet.Tezos
{
    public class TezosWalletScanner : ICurrencyWalletScanner
    {
        private const int DefaultInternalLookAhead = 2;
        private const int OldLookAhead = 4;

        private int InternalLookAhead { get; } = DefaultInternalLookAhead;
        private readonly TezosAccount _account;
        private readonly ILogger? _logger;

        private TezosConfig Config => _account.Config;

        public TezosWalletScanner(TezosAccount account, ILogger? logger = null)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _logger = logger;
        }

        public async Task ScanAsync(
            CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Start balance scan for all XTZ addresses");

            try
            {
                var scanParams = new[]
                {
                    (KeyType : TezosConfig.Bip32Ed25519Key, Chain : Bip44.Internal, LookAhead : OldLookAhead),
                    (KeyType : TezosConfig.Bip32Ed25519Key, Chain : Bip44.External, LookAhead : OldLookAhead),
                    (KeyType : CurrencyConfig.StandardKey, Chain : Bip44.External, LookAhead : InternalLookAhead)
                };

                var tzktApi = new TzktApi(Config.GetTzktSettings());

                var operations = new List<TezosOperation>();
                var walletAddresses = new List<WalletAddress>();

                WalletAddress? defaultWalletAddress = null;

                foreach (var (keyType, chain, lookAhead) in scanParams)
                {
                    var freeKeysCount = 0;
                    var index = 0u;

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var keyPath = Config.GetKeyPathPattern(keyType)
                            .Replace(KeyPathExtensions.AccountPattern, KeyPathExtensions.DefaultAccount)
                            .Replace(KeyPathExtensions.ChainPattern, chain.ToString())
                            .Replace(KeyPathExtensions.IndexPattern, index.ToString());

                        var walletAddress = await _account
                            .DivideAddressAsync(
                                keyPath: keyPath,
                                keyType: keyType)
                            .ConfigureAwait(false);

                        if (keyType == CurrencyConfig.StandardKey &&
                            chain == Bip44.External &&
                            index == 0)
                        {
                            defaultWalletAddress = walletAddress;
                        }

                        var (ops, error) = await UpdateAddressAsync(
                                walletAddress,
                                tzktApi: tzktApi,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (error != null)
                        {
                            _logger?.LogError("Error while scan XTZ address {@address}. Code: {@code}. Message: {@message}",
                                walletAddress.Address,
                                error.Value.Code,
                                error.Value.Message);
                            return;
                        }

                        if (!walletAddress.HasActivity && !ops.Any()) // address without activity
                        {
                            freeKeysCount++;

                            if (freeKeysCount >= lookAhead)
                            {
                                _logger?.LogDebug("{@lookAhead} free keys found for key type {@keyType} and chain {@chain}. Chain scan completed",
                                    lookAhead,
                                    keyType,
                                    chain);
                                break;
                            }
                        }
                        else // address has activity
                        {
                            freeKeysCount = 0;

                            operations.AddRange(ops);

                            // save only active addresses
                            walletAddresses.Add(walletAddress);
                        }

                        index++;
                    }
                }

                if (operations.Any())
                {
                    var upsertResult = await _account
                        .LocalStorage
                        .UpsertTransactionsAsync(
                            txs: operations,
                            notifyIfNewOrChanged: true,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                if (walletAddresses.Count == 0 && defaultWalletAddress != null)
                    walletAddresses.Add(defaultWalletAddress);

                var _ = await _account
                    .LocalStorage
                    .UpsertAddressesAsync(
                        walletAddresses: walletAddresses,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug("Scan canceled");
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Error while scan: {@message}", e.Message);
            }

            _logger?.LogInformation("Balance scan for all XTZ addresses completed");
        }

        public async Task UpdateBalanceAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Start balance update for all XTZ addresses");

            try
            {
                var updateTimeStamp = DateTime.UtcNow;

                var walletAddresses = await _account
                    .GetAddressesAsync(cancellationToken)
                    .ConfigureAwait(false);

                var tzktApi = new TzktApi(Config.GetTzktSettings());
                var operations = new List<TezosOperation>();

                foreach (var walletAddress in walletAddresses)
                {
                    if (skipUsed && walletAddress.IsDisabled)
                        continue;

                    var (ops, error) = await UpdateAddressAsync(
                            walletAddress,
                            tzktApi: tzktApi,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                    {
                        _logger?.LogError("Error while update balance for XTZ address {@address}. Code: {@code}. Message: {@message}",
                            walletAddress.Address,
                            error.Value.Code,
                            error.Value.Message);
                        return;
                    }

                    operations.AddRange(ops);
                }

                if (operations.Any())
                {
                    var _ = await _account
                        .LocalStorage
                        .UpsertTransactionsAsync(
                            txs: operations,
                            notifyIfNewOrChanged: true,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                if (walletAddresses.Any())
                {
                    var _ = await _account
                        .LocalStorage
                        .UpsertAddressesAsync(
                            walletAddresses: walletAddresses,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug("Balance update canceled");
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Error while update balance: {@message}", e.Message);
            }

            _logger?.LogInformation("Balance update for all XTZ addresses completed");
        }

        public async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Start balance update for XTZ address {@address}", address);

            try
            {
                var walletAddress = await _account
                    .LocalStorage
                    .GetAddressAsync(
                        currency: _account.Currency,
                        address: address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (walletAddress == null)
                {
                    _logger?.LogError("Error while balance updating. Can't find address {@address} in local db", address);
                    return;
                }

                var (ops, error) = await UpdateAddressAsync(
                        walletAddress,
                        tzktApi: null,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                {
                    _logger?.LogError("Error while balance updating for XTZ address {@address}. Code: {@code}. Message: {@message}",
                        address,
                        error.Value.Code,
                        error.Value.Message);
                    return;
                }

                if (ops.Any())
                {
                    await _account
                        .LocalStorage
                        .UpsertTransactionsAsync(
                            txs: ops,
                            notifyIfNewOrChanged: true,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
    
                var _ = await _account
                    .LocalStorage
                    .UpsertAddressAsync(walletAddress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug("Balance updating canceled");
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Error while update balance: {@message}", e.Message);
            }

            _logger?.LogInformation("Balance update for XTZ address {@addr} completed", address);
        }

        private async Task<Result<IEnumerable<TezosOperation>>> UpdateAddressAsync(
            WalletAddress walletAddress,
            TzktApi? tzktApi = null,
            CancellationToken cancellationToken = default)
        {
            var updateTimeStamp = DateTime.UtcNow;

            tzktApi ??= new TzktApi(Config.GetTzktSettings());

            var (account, accountError) = await tzktApi
                .GetAccountAsync(walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            if (accountError != null)
            {
                _logger?.LogError("Error while update balance for XTZ address {@address}. Can't get account for address from TzKT. Code: {@code}. Message: {@message}",
                    walletAddress.Address,
                    accountError.Value.Code,
                    accountError.Value.Message);

                return accountError;
            }

            walletAddress.Balance = account?.Balance ?? 0;
            walletAddress.UnconfirmedIncome = 0;
            walletAddress.UnconfirmedOutcome = 0;
            walletAddress.HasActivity = account?.NumberOfTransactions > 0 || account?.TokenBalancesCount > 0;

            var (ops, opsError) = await tzktApi
                .GetOperationsByAddressAsync(
                    address: walletAddress.Address,
                    timeStamp: walletAddress.LastSuccessfullUpdate != DateTime.MinValue
                        ? new DateTimeParameter(walletAddress.LastSuccessfullUpdate, EqualityType.Ge)
                        : null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (opsError != null)
            {
                _logger?.LogError("Error while get transactions for XTZ address {@address}. Code: {@code}. Message: {@message}",
                    walletAddress.Address,
                    opsError.Value.Code,
                    opsError.Value.Message);

                return opsError;
            }

            walletAddress.LastSuccessfullUpdate = updateTimeStamp;

            return new Result<IEnumerable<TezosOperation>> { Value = ops };
        }
    }
}