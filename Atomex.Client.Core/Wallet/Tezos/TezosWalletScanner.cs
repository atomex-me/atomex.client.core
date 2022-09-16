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

                    var tzktApi = new TzktApi(XtzConfig);
                    var updateTimeStamp = DateTime.UtcNow;

                    var txs = new List<TezosTransaction>();
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

                            var txsResult = await UpdateAddressAsync(
                                    walletAddress,
                                    tzktApi: tzktApi,
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);

                            if (txsResult.HasError)
                            {
                                Log.Error("[TezosWalletScanner] ScanAsync error while scan {@address}", walletAddress.Address);
                                return;
                            }

                            if (!walletAddress.HasActivity && !txsResult.Value.Any()) // address without activity
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

                                txs.AddRange(CollapseInternalTransactions(txsResult.Value));

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

                    if (txs.Any())
                    {
                        var upsertResult = await _account
                            .LocalStorage
                            .UpsertTransactionsAsync(
                                txs: txs,
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

                    var tzktApi = new TzktApi(XtzConfig);
                    var txs = new List<TezosTransaction>();

                    foreach (var walletAddress in walletAddresses)
                    {
                        var txsResult = await UpdateAddressAsync(
                                walletAddress,
                                tzktApi: tzktApi,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (txsResult.HasError)
                        {
                            Log.Error("[TezosWalletScanner] UpdateBalanceAsync error while scan {@address}", walletAddress.Address);
                            return;
                        }

                        txs.AddRange(txsResult.Value);
                    }

                    if (txs.Any())
                    {
                        var _ = await _account
                            .LocalStorage
                            .UpsertTransactionsAsync(
                                txs: txs,
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

                    var txsResult = await UpdateAddressAsync(
                            walletAddress,
                            tzktApi: null,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (txsResult.HasError)
                    {
                        Log.Error("[TezosWalletScanner] UpdateBalanceAsync error while scan {@address}", address);
                        return;
                    }

                    if (txsResult.Value.Any())
                    {
                        await _account
                            .LocalStorage
                            .UpsertTransactionsAsync(
                                txs: txsResult.Value,
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

        private async Task<Result<IEnumerable<TezosTransaction>>> UpdateAddressAsync(
            WalletAddress walletAddress,
            TzktApi tzktApi = null,
            CancellationToken cancellationToken = default)
        {
            var updateTimeStamp = DateTime.UtcNow;

            if (tzktApi == null)
                tzktApi = new TzktApi(XtzConfig);

            var accountResult = await tzktApi
                .GetAccountAsync(walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            if (accountResult.HasError)
            {
                Log.Error("[TezosWalletScanner] ScanAddressAsync error. Can't get account for address: {@address}. Code: {@code}. Description: {@description}",
                    walletAddress.Address,
                    accountResult.Error.Code,
                    accountResult.Error.Description);

                return accountResult.Error;
            }

            walletAddress.Balance = accountResult.Value.Balance.ToTez();
            walletAddress.UnconfirmedIncome = 0;
            walletAddress.UnconfirmedOutcome = 0;
            walletAddress.HasActivity = accountResult.Value.NumberOfTransactions > 0 || accountResult.Value.TokenBalancesCount > 0;

            var txsResult = await tzktApi
                .GetTransactionsAsync(
                    address: walletAddress.Address,
                    fromTimeStamp: walletAddress.LastSuccessfullUpdate != DateTime.MinValue
                        ? walletAddress.LastSuccessfullUpdate
                        : null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsResult.HasError)
            {
                Log.Error("[TezosWalletScanner] UpdateBalanceAsync error. Can't get txs for address: {@address}. Code: {@code}. Description: {@description}",
                    walletAddress.Address,
                    txsResult.Error.Code,
                    txsResult.Error.Description);

                return txsResult.Error;
            }

            var txs = CollapseInternalTransactions(txsResult.Value);

            walletAddress.LastSuccessfullUpdate = updateTimeStamp;

            return new Result<IEnumerable<TezosTransaction>>(txs);
        }

        private IEnumerable<TezosTransaction> CollapseInternalTransactions(
            IEnumerable<TezosTransaction> txs)
        {
            var result = new List<TezosTransaction>();
            var txsById = new Dictionary<string, TezosTransaction>();
            var internalTxs = new List<TezosTransaction>();

            foreach (var tx in txs)
            {
                if (tx.IsInternal)
                {
                    internalTxs.Add(tx);
                }
                else
                {
                    result.Add(tx);
                    txsById.TryAdd(tx.Id, tx);
                }
            }

            foreach (var internalTx in internalTxs)
            {
                if (txsById.TryGetValue(internalTx.Id, out var tx))
                {
                    if (tx.InternalTxs == null)
                        tx.InternalTxs = new List<TezosTransaction>();

                    tx.InternalTxs.Add(internalTx);
                }
                else
                {
                    result.Add(internalTx);
                }
            }

            return result;
        }
    }
}