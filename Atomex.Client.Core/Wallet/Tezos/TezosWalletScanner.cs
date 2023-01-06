using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.Tezos
{
    public class TezosWalletScanner : ICurrencyWalletScanner
    {
        private const int DefaultInternalLookAhead = 2;
        private const int OldLookAhead = 4;

        private int InternalLookAhead { get; } = DefaultInternalLookAhead;
        private readonly TezosAccount _account;
        private TezosConfig XtzConfig => _account.Config;

        public TezosWalletScanner(TezosAccount account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var scanParams = new[]
                    {
                        (KeyType : TezosConfig.Bip32Ed25519Key, Chain : Bip44.Internal, LookAhead : OldLookAhead),
                        (KeyType : TezosConfig.Bip32Ed25519Key, Chain : Bip44.External, LookAhead : OldLookAhead),
                        (KeyType : CurrencyConfig.StandardKey, Chain : Bip44.External, LookAhead : InternalLookAhead)
                    };

                    var tzktApi = new TzktApi(XtzConfig.GetTzktSettings());
                    var updateTimeStamp = DateTime.UtcNow;

                    var operations = new List<TezosOperation>();
                    var walletAddresses = new List<WalletAddress>();

                    WalletAddress defautWalletAddress = null;

                    foreach (var (keyType, chain, lookAhead) in scanParams)
                    {
                        var freeKeysCount = 0;
                        var account = 0u;
                        var index = 0u;

                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var walletAddress = await _account
                                .DivideAddressAsync(
                                    account: account,
                                    chain: chain,
                                    index: index,
                                    keyType: keyType)
                                .ConfigureAwait(false);

                            if (walletAddress.KeyType == CurrencyConfig.StandardKey &&
                                walletAddress.KeyIndex.Chain == Bip44.External &&
                                walletAddress.KeyIndex.Account == 0 &&
                                walletAddress.KeyIndex.Index == 0)
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

                            if (keyType == TezosConfig.Bip32Ed25519Key)
                            {
                                index++;
                            }
                            else
                            {
                                account++;
                            }
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

            }, cancellationToken);
        }

        public Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return UpdateBalanceAsync(address, cancellationToken);
        }

        public Task UpdateBalanceAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var updateTimeStamp = DateTime.UtcNow;

                    var walletAddresses = await _account
                        .GetAddressesAsync(cancellationToken)
                        .ConfigureAwait(false);

                    // todo: if skipUsed == true => skip "disabled" wallets

                    var tzktApi = new TzktApi(XtzConfig.GetTzktSettings());
                    var operations = new List<TezosOperation>();

                    foreach (var walletAddress in walletAddresses)
                    {
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

            }, cancellationToken);
        }

        public Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
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

            }, cancellationToken);
        }

        private async Task<Result<IEnumerable<TezosOperation>>> UpdateAddressAsync(
            WalletAddress walletAddress,
            TzktApi tzktApi = null,
            CancellationToken cancellationToken = default)
        {
            var updateTimeStamp = DateTime.UtcNow;

            tzktApi ??= new TzktApi(XtzConfig.GetTzktSettings());

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

            walletAddress.Balance = account.Balance.ToTez();
            walletAddress.UnconfirmedIncome = 0;
            walletAddress.UnconfirmedOutcome = 0;
            walletAddress.HasActivity = account.NumberOfTransactions > 0 || account.TokenBalancesCount > 0;

            var (ops, opsError) = await tzktApi
                .GetOperationsAsync(
                    address: walletAddress.Address,
                    fromTimeStamp: walletAddress.LastSuccessfullUpdate != DateTime.MinValue
                        ? walletAddress.LastSuccessfullUpdate
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