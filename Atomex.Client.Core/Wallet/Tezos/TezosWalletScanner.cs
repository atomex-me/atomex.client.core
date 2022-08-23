using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Tzkt;
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

                            var addressTxs = await ScanAddressAsync(
                                    walletAddress,
                                    tzktApi: tzktApi,
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);

                            if (addressTxs == null)
                            {
                                Log.Error("[TezosWalletScanner] ScanAsync error: error while scan {@address}", walletAddress.Address);
                                return;
                            }

                            txs.AddRange(addressTxs);
                        }
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

                    //if (walletAddresses.Any())
                    //{
                    //    var _ = await _account
                    //        .LocalStorage
                    //        .UpsertAddressesAsync(walletAddresses)
                    //        .ConfigureAwait(false);
                    //}
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
            //var currency = Currency;

            //var tezosAddresses = await Account.DataRepository
            //    .GetAddressesAsync(currency.Name)
            //    .ConfigureAwait(false);

            //var isFirstScan = tezosAddresses.Count() <= 1;

            //var scanBip32Ed25519 = isFirstScan || tezosAddresses
            //    .FirstOrDefault(w => w.KeyType == TezosConfig.Bip32Ed25519Key &&
            //                         (w.HasActivity ||
            //                         w.Balance != 0 ||
            //                         w.UnconfirmedIncome != 0 ||
            //                         w.UnconfirmedOutcome != 0)) != null;

            //var scanParams = scanBip32Ed25519
            //    ? new[]
            //    {
            //        (KeyType : TezosConfig.Bip32Ed25519Key, Chain : Bip44.Internal, LookAhead : OldLookAhead),
            //        (KeyType : TezosConfig.Bip32Ed25519Key, Chain : Bip44.External, LookAhead : OldLookAhead),
            //        (KeyType : CurrencyConfig.StandardKey, Chain : Bip44.External, LookAhead : InternalLookAhead)
            //    }
            //    : new[]
            //    {
            //        (KeyType : CurrencyConfig.StandardKey, Chain : Bip44.External, LookAhead : ExternalLookAhead),
            //    };

            //var txs = new List<TezosTransaction>();
            //var txsById = new Dictionary<string, TezosTransaction>();
            //var internalTxs = new List<TezosTransaction>();

            //foreach (var (keyType, chain, lookAhead) in scanParams)
            //{
            //    var freeKeysCount = 0;
            //    var account = 0u;
            //    var index = 0u;

            //    while (true)
            //    {
            //        cancellationToken.ThrowIfCancellationRequested();

            //        var walletAddress = await Account
            //            .DivideAddressAsync(
            //                account: account,
            //                chain: chain,
            //                index: index,
            //                keyType: keyType)
            //            .ConfigureAwait(false);

            //        if (walletAddress == null)
            //            break;

            //        Log.Debug(
            //            "Scan transactions for {@name} address {@chain}:{@index}:{@address}",
            //            currency.Name,
            //            chain,
            //            index,
            //            walletAddress.Address);

            //        var addressTxs = await ScanAddressAsync(walletAddress.Address, cancellationToken)
            //            .ConfigureAwait(false);

            //        if (addressTxs == null || !addressTxs.Any()) // address without activity
            //        {
            //            freeKeysCount++;

            //            if (freeKeysCount >= lookAhead)
            //            {
            //                Log.Debug("{@lookAhead} free keys found. Chain scan completed", lookAhead);
            //                break;
            //            }
            //        }
            //        else // address has activity
            //        {
            //            freeKeysCount = 0;

            //            foreach (var tx in addressTxs)
            //            {
            //                if (tx.IsInternal)
            //                {
            //                    internalTxs.Add(tx);
            //                }
            //                else if (!txsById.ContainsKey(tx.Id))
            //                {
            //                    txsById.Add(tx.Id, tx);
            //                }
            //            }
            //        }

            //        if (keyType == TezosConfig.Bip32Ed25519Key)
            //        {
            //            index++;
            //        }
            //        else
            //        {
            //            account++;
            //        }
            //    }
            //}

            //// distribute internal txs
            //foreach (var internalTx in internalTxs)
            //{
            //    if (txsById.TryGetValue(internalTx.Id, out var tx))
            //    {
            //        if (tx.InternalTxs == null)
            //            tx.InternalTxs = new List<TezosTransaction>();

            //        tx.InternalTxs.Add(internalTx);
            //    }
            //    else
            //    {
            //        txs.Add(internalTx);
            //    }
            //}

            //txs.AddRange(txsById.Values);

            //if (txs.Any())
            //{
            //    await UpsertTransactionsAsync(txs)
            //        .ConfigureAwait(false);
            //}

            //await Account
            //    .UpdateBalanceAsync(cancellationToken)
            //    .ConfigureAwait(false);

            //var needToCheckBip32Ed25519 = isFirstScan && scanBip32Ed25519;

            //if (!needToCheckBip32Ed25519)
            //    return;

            //var addresses = (await Account.DataRepository
            //    .GetAddressesAsync(currency.Name)
            //    .ConfigureAwait(false))
            //    .Where(a => a.KeyType == TezosConfig.Bip32Ed25519Key);

            //// if there is at least one address with activity => leave bip32ed25519 addresses in db
            //if (addresses.Any(w => w.HasActivity || w.Balance > 0 || w.UnconfirmedIncome > 0 || w.UnconfirmedOutcome > 0))
            //    return;

            //// remove bip32Ed25519 addresses if there is no activity on them
            //foreach (var address in addresses.ToList())
            //{
            //    _ = await Account.DataRepository
            //        .RemoveAddressAsync(address.Currency, address.Address)
            //        .ConfigureAwait(false);
            //}
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
                        var addressTxs = await ScanAddressAsync(
                                walletAddress,
                                tzktApi: tzktApi,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (addressTxs == null)
                        {
                            Log.Error("[TezosWalletScanner] UpdateBalanceAsync error: error while scan {@address}", walletAddress.Address);
                            return;
                        }

                        txs.AddRange(addressTxs);
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
                        Log.Error("[TezosWalletScanner] UpdateBalanceAsync error: can't find address {@address} in local db", address);
                        return;
                    }

                    var txs = await ScanAddressAsync(
                            walletAddress,
                            tzktApi: null,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (txs == null)
                    {
                        Log.Error("[TezosWalletScanner] UpdateBalanceAsync error: error while scan {@address}", address);
                        return;
                    }

                    if (txs.Any())
                    {
                        await _account
                            .LocalStorage
                            .UpsertTransactionsAsync(
                                txs: txs,
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

        private async Task<IEnumerable<TezosTransaction>> ScanAddressAsync(
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

            if (accountResult == null)
            {
                Log.Error("[TezosWalletScanner] ScanAddressAsync error: can't get account for address {@address}", walletAddress.Address);
                return null;
            }

            if (accountResult.HasError)
            {
                Log.Error("[TezosWalletScanner] ScanAddressAsync error: can't get account for {@address}. Code: {@code}. Description: {@description}",
                    walletAddress.Address,
                    accountResult.Error.Code,
                    accountResult.Error.Description);

                return null;
            }

            walletAddress.Balance = accountResult.Value.Balance.ToTez();
            walletAddress.UnconfirmedIncome = 0;
            walletAddress.UnconfirmedOutcome = 0;
            walletAddress.HasActivity = accountResult.Value.NumberOfTransactions > 0 || accountResult.Value.TokenBalancesCount > 0;

            var txsResult = await tzktApi
                .GetTransactionsAsync(
                    address: walletAddress.Address,
                    fromTimeStamp: walletAddress.LastSuccessfullUpdate,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsResult == null)
            {
                Log.Error("[TezosWalletScanner] UpdateBalanceAsync error: can't get txs for address {@address}", walletAddress.Address);
                return null;
            }

            if (txsResult.HasError)
            {
                Log.Error("[TezosWalletScanner] UpdateBalanceAsync error: can't get txs for {@address}. Code: {@code}. Description: {@description}",
                    walletAddress.Address,
                    accountResult.Error.Code,
                    accountResult.Error.Description);

                return null;
            }

            var txs = CollapseInternalTransactions(txsResult.Value);

            walletAddress.LastSuccessfullUpdate = updateTimeStamp;

            return txs;
        }

        private IEnumerable<TezosTransaction> CollapseInternalTransactions(IEnumerable<TezosTransaction> txs)
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