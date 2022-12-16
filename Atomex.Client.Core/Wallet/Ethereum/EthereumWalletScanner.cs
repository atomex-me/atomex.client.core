using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.Ethereum
{
    public class EthereumWalletScanner : ICurrencyWalletScanner
    {
        private const int DefaultInternalLookAhead = 1;
        private const int DefaultExternalLookAhead = 1;

        private int InternalLookAhead { get; } = DefaultInternalLookAhead;
        private int ExternalLookAhead { get; } = DefaultExternalLookAhead;
        private readonly EthereumAccount _account;
        private EthereumConfig EthConfig => _account.EthConfig;

        public EthereumWalletScanner(EthereumAccount account)
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
                        (Chain : Bip44.Internal, LookAhead : InternalLookAhead),
                        (Chain : Bip44.External, LookAhead : ExternalLookAhead),
                    };

                    var api = EthConfig.BlockchainApi;
                    var transactions = new List<EthereumTransaction>();
                    var walletAddresses = new List<WalletAddress>();

                    WalletAddress defautWalletAddress = null;

                    foreach (var (chain, lookAhead) in scanParams)
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
                                    keyType: CurrencyConfig.StandardKey)
                                .ConfigureAwait(false);

                            if (walletAddress.KeyType == CurrencyConfig.StandardKey &&
                                walletAddress.KeyIndex.Chain == Bip44.External &&
                                walletAddress.KeyIndex.Account == 0 &&
                                walletAddress.KeyIndex.Index == 0)
                            {
                                defautWalletAddress = walletAddress;
                            }

                            var (txs, error) = await UpdateAddressAsync(
                                    walletAddress: walletAddress,
                                    api: api,
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);

                            if (error != null)
                            {
                                Log.Error("[EthereumWalletScanner] ScanAsync error while scan {@address}", walletAddress.Address);
                                return;
                            }

                            if (!walletAddress.HasActivity && !txs.Any()) // address without activity
                            {
                                freeKeysCount++;

                                if (freeKeysCount >= lookAhead)
                                {
                                    Log.Debug("[EthereumWalletScanner] {@lookAhead} free keys found. Chain scan completed", lookAhead);
                                    break;
                                }
                            }
                            else // address has activity
                            {
                                freeKeysCount = 0;

                                transactions.AddRange(txs);

                                // save only active addresses
                                walletAddresses.Add(walletAddress);
                            }

                            index++;
                        }
                    }

                    if (transactions.Any())
                    {
                        var upsertResult = await _account
                            .LocalStorage
                            .UpsertTransactionsAsync(
                                txs: transactions,
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
                    Log.Debug("[EthereumWalletScanner] ScanAsync canceled");
                }
                catch (Exception e)
                {
                    Log.Error(e, "[EthereumWalletScanner] ScanAsync error: {@message}", e.Message);
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

                    var api = EthConfig.BlockchainApi;
                    var transactions = new List<EthereumTransaction>();

                    foreach (var walletAddress in walletAddresses)
                    {
                        var (txs, error) = await UpdateAddressAsync(
                                walletAddress,
                                api: api,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (error != null)
                        {
                            Log.Error("[EthereumWalletScanner] UpdateBalanceAsync error while scan {@address}", walletAddress.Address);
                            return;
                        }

                        transactions.AddRange(txs);
                    }

                    if (transactions.Any())
                    {
                        var _ = await _account
                            .LocalStorage
                            .UpsertTransactionsAsync(
                                txs: transactions,
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
                    Log.Debug("[EthereumWalletScanner] UpdateBalanceAsync canceled");
                }
                catch (Exception e)
                {
                    Log.Error(e, "[EthereumWalletScanner] UpdateBalanceAsync error: {@message}", e.Message);
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
                    Log.Debug("[EthereumWalletScanner] UpdateBalanceAsync for address {@address}", address);

                    var walletAddress = await _account
                        .LocalStorage
                        .GetWalletAddressAsync(_account.Currency, address)
                        .ConfigureAwait(false);

                    if (walletAddress == null)
                    {
                        Log.Error("[EthereumWalletScanner] UpdateBalanceAsync error. Can't find address {@address} in local db", address);
                        return;
                    }

                    var (txs, error) = await UpdateAddressAsync(
                            walletAddress,
                            api: null,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                    {
                        Log.Error("[EthereumWalletScanner] UpdateBalanceAsync error while scan {@address}", address);
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
                    Log.Debug("[EthereumWalletScanner] UpdateBalanceAsync canceled");
                }
                catch (Exception e)
                {
                    Log.Error(e, "[EthereumWalletScanner] UpdateBalanceAsync error: {@message}", e.Message);
                }

            }, cancellationToken);
        }

        private async Task<Result<IEnumerable<EthereumTransaction>>> UpdateAddressAsync(
            WalletAddress walletAddress,
            IBlockchainApi api = null,
            CancellationToken cancellationToken = default)
        {
            var updateTimeStamp = DateTime.UtcNow;

            if (api == null)
                api = EthConfig.BlockchainApi;

            var (balance, getBalanceError) = await api
                .GetBalanceAsync(
                    address: walletAddress.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (getBalanceError != null)
            {
                Log.Error("[EthereumWalletScanner] UpdateAddressAsync error while getting balance for {@address} with code {@code} and description {@description}",
                    walletAddress.Address,
                    getBalanceError.Value.Code,
                    getBalanceError.Value.Message);

                return getBalanceError;
            }

            var (txs, getTxsError) = await ((IEthereumApi)api)
                .GetTransactionsAsync(walletAddress.Address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (getTxsError != null)
            {
                Log.Error(
                    "[EthereumWalletScanner] UpdateAddressAsync error while scan address transactions for {@address} with code {@code} and description {@description}",
                    walletAddress.Address,
                    getTxsError.Value.Code,
                    getTxsError.Value.Message);

                return getTxsError;
            }

            walletAddress.Balance = balance;
            walletAddress.UnconfirmedIncome = 0;
            walletAddress.UnconfirmedOutcome = 0;
            walletAddress.HasActivity = txs.Any();

            walletAddress.LastSuccessfullUpdate = updateTimeStamp;

            return new Result<IEnumerable<EthereumTransaction>> { Value = txs };
        }
    }
}