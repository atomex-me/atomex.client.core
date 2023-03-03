using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.EtherScan;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallets.Bips;

namespace Atomex.Wallet.Ethereum
{
    public class EthereumWalletScanner : ICurrencyWalletScanner
    {
        private const int DefaultInternalLookAhead = 1;
        private const int DefaultExternalLookAhead = 1;

        private int InternalLookAhead { get; } = DefaultInternalLookAhead;
        private int ExternalLookAhead { get; } = DefaultExternalLookAhead;
        private readonly EthereumAccount _account;
        private EthereumConfig Config => _account.EthConfig;

        public EthereumWalletScanner(EthereumAccount account)
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
                    (Chain : Bip44.Internal, LookAhead : InternalLookAhead),
                    (Chain : Bip44.External, LookAhead : ExternalLookAhead),
                };

                var api = Config.GetEtherScanApi();
                var transactions = new List<EthereumTransaction>();
                var walletAddresses = new List<WalletAddress>();

                WalletAddress defautWalletAddress = null;

                var keyType = CurrencyConfig.StandardKey;

                foreach (var (chain, lookAhead) in scanParams)
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
                    .UpsertAddressesAsync(_account.Currency, walletAddresses)
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

                var api = Config.GetEtherScanApi();
                var transactions = new List<EthereumTransaction>();

                foreach (var walletAddress in walletAddresses)
                {
                    if (skipUsed && walletAddress.IsDisabled)
                        continue;

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
                        .UpsertAddressesAsync(_account.Currency, walletAddresses, cancellationToken)
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
        }

        public async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("[EthereumWalletScanner] UpdateBalanceAsync for address {@address}", address);

                var walletAddress = await _account
                    .LocalStorage
                    .GetAddressAsync(
                        currency: _account.Currency,
                        address: address,
                        cancellationToken: cancellationToken)
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
                    .UpsertAddressAsync(walletAddress, cancellationToken)
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
        }

        private async Task<Result<IEnumerable<EthereumTransaction>>> UpdateAddressAsync(
            WalletAddress walletAddress,
            EtherScanApi api = null,
            CancellationToken cancellationToken = default)
        {
            var updateTimeStamp = DateTime.UtcNow;

            api ??= Config.GetEtherScanApi();

            var (balance, getBalanceError) = await api
                .GetBalanceAsync(
                    address: walletAddress.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (getBalanceError != null)
            {
                Log.Error("[EthereumWalletScanner] UpdateAddressAsync error while getting balance for {@address} with code {@code} and message {@message}",
                    walletAddress.Address,
                    getBalanceError.Value.Code,
                    getBalanceError.Value.Message);

                return getBalanceError;
            }

            var fromBlock = 0UL;

            if (walletAddress.LastSuccessfullUpdate != DateTime.MinValue)
            {
                var (blockNumber, blockNumberError) = await api
                    .GetBlockNumberAsync(
                        timeStamp: walletAddress.LastSuccessfullUpdate,
                        blockClosest: ClosestBlock.Before,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (blockNumberError != null)
                {
                    Log.Error(
                        "[EthereumWalletScanner] Error while getting block number with code {@code} and message {@message}",
                        blockNumberError.Value.Code,
                        blockNumberError.Value.Message);

                    return blockNumberError;
                }

                fromBlock = blockNumber;
            }

            var (txs, getTxsError) = await api
                .GetTransactionsAsync(
                    address: walletAddress.Address,
                    fromBlock: fromBlock,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (getTxsError != null)
            {
                Log.Error(
                    "[EthereumWalletScanner] UpdateAddressAsync error while scan address transactions for {@address} with code {@code} and message {@message}",
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