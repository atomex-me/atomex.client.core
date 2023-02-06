using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Common;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallets.Bips;

namespace Atomex.Wallet.Tezos
{
    public class TezosWalletScanner : ICurrencyWalletScanner
    {
        private const int DefaultInternalLookAhead = 2;
        private const int OldLookAhead = 4;

        private int InternalLookAhead { get; } = DefaultInternalLookAhead;
        private readonly TezosAccount _account;
        private TezosConfig Config => _account.Config;

        public TezosWalletScanner(TezosAccount account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task ScanAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var scanParams = new[]
                {
                    (KeyType : TezosConfig.Bip32Ed25519Key, Chain : Bip44.Internal, LookAhead : OldLookAhead),
                    (KeyType : TezosConfig.Bip32Ed25519Key, Chain : Bip44.External, LookAhead : OldLookAhead),
                    (KeyType : CurrencyConfig.StandardKey, Chain : Bip44.External, LookAhead : InternalLookAhead)
                };

                var tzktApi = new TzktApi(Config.GetTzktSettings());
                var updateTimeStamp = DateTime.UtcNow;

                var operations = new List<TezosOperation>();
                var walletAddresses = new List<WalletAddress>();

                WalletAddress defautWalletAddress = null;

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
                            defautWalletAddress = walletAddress;
                        }

                        var (ops, error) = await UpdateAddressAsync(
                                walletAddress,
                                tzktApi: tzktApi,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (error != null)
                        {
                            Log.Error("[TezosWalletScanner] ScanAsync error while scan {@address}", walletAddress.Address);
                            return;
                        }

                        if (!walletAddress.HasActivity && !ops.Any()) // address without activity
                        {
                            freeKeysCount++;

                            if (freeKeysCount >= lookAhead)
                            {
                                Log.Debug("{@lookAhead} free keys found. Chain scan completed", lookAhead);
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

                if (!walletAddresses.Any())
                    walletAddresses.Add(defautWalletAddress);

                var _ = await _account
                    .LocalStorage
                    .UpsertAddressesAsync(walletAddresses)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("[TezosWalletScanner] ScanAsync canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "[TezosWalletScanner] ScanAsync error: {@message}", e.Message);
            }
        }

        public Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return UpdateBalanceAsync(address, cancellationToken);
        }

        public async Task UpdateBalanceAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
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
                        Log.Error("[TezosWalletScanner] UpdateBalanceAsync error while scan {@address}", walletAddress.Address);
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
                        .UpsertAddressesAsync(walletAddresses)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("[TezosWalletScanner] UpdateBalanceAsync canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "[TezosWalletScanner] UpdateBalanceAsync error: {@message}", e.Message);
            }
        }

        public async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("UpdateBalanceAsync for address {@address}", address);

                var walletAddress = await _account
                    .LocalStorage
                    .GetWalletAddressAsync(_account.Currency, address)
                    .ConfigureAwait(false);

                if (walletAddress == null)
                {
                    Log.Error("[TezosWalletScanner] UpdateBalanceAsync error. Can't find address {@address} in local db", address);
                    return;
                }

                var (ops, error) = await UpdateAddressAsync(
                        walletAddress,
                        tzktApi: null,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                {
                    Log.Error("[TezosWalletScanner] UpdateBalanceAsync error while scan {@address}", address);
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
                    .UpsertAddressAsync(walletAddress)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("[TezosWalletScanner] UpdateBalanceAsync canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "[TezosWalletScanner] UpdateBalanceAsync error: {@message}", e.Message);
            }
        }

        private async Task<Result<IEnumerable<TezosOperation>>> UpdateAddressAsync(
            WalletAddress walletAddress,
            TzktApi tzktApi = null,
            CancellationToken cancellationToken = default)
        {
            var updateTimeStamp = DateTime.UtcNow;

            tzktApi ??= new TzktApi(Config.GetTzktSettings());

            var (account, accountError) = await tzktApi
                .GetAccountAsync(walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            if (accountError != null)
            {
                Log.Error("[TezosWalletScanner] ScanAddressAsync error. Can't get account for address: {@address}. Code: {@code}. Description: {@description}",
                    walletAddress.Address,
                    accountError.Value.Code,
                    accountError.Value.Message);

                return accountError;
            }

            walletAddress.Balance = account.Balance;
            walletAddress.UnconfirmedIncome = 0;
            walletAddress.UnconfirmedOutcome = 0;
            walletAddress.HasActivity = account.NumberOfTransactions > 0 || account.TokenBalancesCount > 0;

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
                Log.Error("[TezosWalletScanner] UpdateBalanceAsync error. Can't get txs for address: {@address}. Code: {@code}. Description: {@description}",
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