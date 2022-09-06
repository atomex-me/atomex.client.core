﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Bip;

namespace Atomex.Wallet.BitcoinBased
{
    public class BitcoinBasedWalletScanner : ICurrencyWalletScanner
    {
        private const int DefaultInternalLookAhead = 3;
        private const int DefaultExternalLookAhead = 3;

        private readonly BitcoinBasedAccount _account;
        private CurrencyConfig BitcoinBasedConfig => _account.Currencies.GetByName(_account.Currency);
        private int InternalLookAhead { get; } = DefaultInternalLookAhead;
        private int ExternalLookAhead { get; } = DefaultExternalLookAhead;

        public BitcoinBasedWalletScanner(BitcoinBasedAccount account)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            //await ScanOutputsAsync(
            //        skipUsed: skipUsed,
            //        cancellationToken: cancellationToken)
            //    .ConfigureAwait(false);

            //await ScanTransactionsAsync(cancellationToken)
            //    .ConfigureAwait(false);

            //await _account
            //    .UpdateBalanceAsync(cancellationToken)
            //    .ConfigureAwait(false);
        }

        [Obsolete("Use UpdateBalanceAsync instead")]
        public Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return UpdateBalanceAsync(address, cancellationToken);
            //var currency = Currency;

            //Log.Debug("Scan {@currency} outputs for {@address}",
            //    currency.Name,
            //    address);

            //var outputsResult = await ((IInOutBlockchainApi)currency.BlockchainApi)
            //    .TryGetOutputsAsync(address, cancellationToken: cancellationToken)
            //    .ConfigureAwait(false);

            //if (outputsResult == null)
            //{
            //    Log.Error("Connection error while scan outputs for {@address}", address);
            //    return;
            //}

            //if (outputsResult.HasError)
            //{
            //    Log.Error(
            //        "Error while scan outputs for {@address} with code {@code} and description {@description}",
            //        address,
            //        outputsResult.Error.Code,
            //        outputsResult.Error.Description);

            //    return;
            //}

            //var outputs = outputsResult.Value?.RemoveDuplicates().ToList();

            //if (outputs == null || !outputs.Any())
            //    return;

            ////await Account
            ////    .LocalStorage
            ////    .UpsertOutputsAsync(
            ////        outputs: outputs,
            ////        currency: currency.Name,
            ////        address: address)
            ////    .ConfigureAwait(false);

            //await ScanTransactionsAsync(outputs, cancellationToken)
            //    .ConfigureAwait(false);

            //await Account
            //    .UpdateBalanceAsync(address, cancellationToken)
            //    .ConfigureAwait(false);
        }

        public Task UpdateBalanceAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    Log.Debug("[BitcoinBasedWalletScanner] UpdateBalanceAsync for address {@address}", address);

                    var walletAddress = await _account
                        .LocalStorage
                        .GetWalletAddressAsync(_account.Currency, address)
                        .ConfigureAwait(false);

                    if (walletAddress == null)
                    {
                        Log.Error("[BitcoinBasedWalletScanner] UpdateBalanceAsync error. Can't find address {@address} in local db", address);
                        return;
                    }

                    var outputsResult = await UpdateAddressAsync(
                            walletAddress,
                            api: null,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (outputsResult.HasError)
                    {
                        Log.Error("[BitcoinBasedWalletScanner] UpdateBalanceAsync error while scan {@address}", address);
                        return;
                    }

                    if (outputsResult.Value.Any())
                    {
                        await _account
                            .LocalStorage
                            .UpsertOutputsAsync(
                                outputs: outputsResult.Value,
                                currency: _account.Currency,
                                address: address)
                            .ConfigureAwait(false);
                    }

                    var _ = await _account
                        .LocalStorage
                        .UpsertAddressAsync(walletAddress)
                        .ConfigureAwait(false);

                }
                catch (OperationCanceledException)
                {
                    Log.Debug("[BitcoinBasedWalletScanner] UpdateBalanceAsync canceled");
                }
                catch (Exception e)
                {
                    Log.Error(e, "[BitcoinBasedWalletScanner] UpdateBalanceAsync error: {@message}", e.Message);
                }

            }, cancellationToken);
        }

        private async Task<Result<IEnumerable<BitcoinBasedTxOutput>>> UpdateAddressAsync(
            WalletAddress walletAddress,
            IBlockchainApi api = null,
            CancellationToken cancellationToken = default)
        {
            var updateTimeStamp = DateTime.UtcNow;

            var balanceResult = await api
                .TryGetBalanceAsync(
                    address: walletAddress.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (balanceResult.HasError)
            {
                Log.Error("[BitcoinBasedWalletScanner] UpdateAddressAsync error while getting balance for {@address} with code {@code} and description {@description}",
                    walletAddress.Address,
                    balanceResult.Error.Code,
                    balanceResult.Error.Description);

                return balanceResult.Error;
            }

            var getOutputsResult = await ((BitcoinBasedBlockchainApi)BitcoinBasedConfig.BlockchainApi)
                .TryGetOutputsAsync(walletAddress.Address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (getOutputsResult.HasError)
            {
                Log.Error(
                    "[BitcoinBasedWalletScanner] Error while scan outputs for {@address} with code {@code} and description {@description}",
                    walletAddress.Address,
                    getOutputsResult.Error.Code,
                    getOutputsResult.Error.Description);

                return getOutputsResult.Error;
            }

            //getOutputsResult.Value.Sum(o => o.)

            walletAddress.Balance = balanceResult.Value;
            walletAddress.UnconfirmedIncome = 0;
            walletAddress.UnconfirmedOutcome = 0;
            walletAddress.HasActivity = getOutputsResult.Value.Any();
            walletAddress.LastSuccessfullUpdate = updateTimeStamp;

            return new Result<IEnumerable<BitcoinBasedTxOutput>>(getOutputsResult.Value);
        }

        //private async Task ScanOutputsAsync(
        //   bool skipUsed,
        //   CancellationToken cancellationToken = default)
        //{
        //    var currency = BitcoinBasedConfig;

        //    Log.Debug("Scan outputs for {@name}", currency.Name);

        //    var scanParams = new[]
        //    {
        //        new {Chain = Bip44.Internal, LookAhead = InternalLookAhead},
        //        new {Chain = Bip44.External, LookAhead = ExternalLookAhead},
        //    };

        //    foreach (var param in scanParams)
        //    {
        //        var freeKeysCount = 0;
        //        var index = 0u;

        //        while (true)
        //        {
        //            cancellationToken.ThrowIfCancellationRequested();

        //            var walletAddress = await _account
        //                .DivideAddressAsync(
        //                    account: Bip44.DefaultAccount,
        //                    chain: param.Chain,
        //                    index: index,
        //                    keyType: CurrencyConfig.StandardKey)
        //                .ConfigureAwait(false);

        //            if (walletAddress == null)
        //                break;

        //            if (skipUsed) // check, if the address marked as "used" and skip in this case
        //            {
        //                var resolvedAddress = await _account
        //                    .GetAddressAsync(currency.Name, walletAddress.Address, cancellationToken)
        //                    .ConfigureAwait(false);

        //                if (resolvedAddress != null &&
        //                    resolvedAddress.HasActivity &&
        //                    resolvedAddress.Balance == 0)
        //                {
        //                    freeKeysCount = 0;
        //                    index++;

        //                    continue;
        //                }
        //            }

        //            Log.Debug(
        //                "Scan outputs for {@name} address {@chain}:{@index}:{@address}",
        //                currency.Name,
        //                param.Chain,
        //                index,
        //                walletAddress.Address);

        //            var result = await ((IInOutBlockchainApi)currency.BlockchainApi)
        //                .TryGetOutputsAsync(walletAddress.Address, cancellationToken: cancellationToken)
        //                .ConfigureAwait(false);

        //            if (result == null)
        //            {
        //                Log.Error("Error while scan outputs for {@address}", walletAddress.Address);
        //                break;
        //            }

        //            if (result.HasError)
        //            {
        //                Log.Error(
        //                    "Error while scan outputs for {@address} with code {@code} and description {@description}",
        //                    walletAddress.Address,
        //                    result.Error.Code,
        //                    result.Error.Description);
        //                break;
        //            }

        //            var outputs = result.Value?.RemoveDuplicates().ToList();

        //            if (outputs == null || !outputs.Any()) // address without activity
        //            {
        //                freeKeysCount++;

        //                if (freeKeysCount >= param.LookAhead)
        //                {
        //                    Log.Debug($"{param.LookAhead} free keys found. Chain scan completed");
        //                    break;
        //                }
        //            }
        //            else // address has activity
        //            {
        //                freeKeysCount = 0;

        //                //await Account
        //                //    .UpsertOutputsAsync(
        //                //        outputs: outputs,
        //                //        address: walletAddress.Address,
        //                //        notifyIfBalanceUpdated: false)
        //                //    .ConfigureAwait(false);
        //            }

        //            index++;
        //        }
        //    }
        //}

        //private async Task ScanTransactionsAsync(
        //    CancellationToken cancellationToken = default)
        //{
        //    var outputs = await _account
        //        .GetOutputsAsync()
        //        .ConfigureAwait(false);

        //    await ScanTransactionsAsync(outputs, cancellationToken)
        //        .ConfigureAwait(false);
        //}

        //private async Task ScanTransactionsAsync(
        //    IEnumerable<BitcoinBasedTxOutput> outputs,
        //    CancellationToken cancellationToken = default)
        //{
        //    var currency = BitcoinBasedConfig;

        //    foreach (var output in outputs)
        //    {
        //        cancellationToken.ThrowIfCancellationRequested();

        //        var txIds = output.IsSpent
        //            ? new[] { output.TxId, output.SpentTxPoint.Hash }
        //            : new[] { output.TxId };

        //        foreach (var txId in txIds)
        //        {
        //            var localTx = await _account
        //                .LocalStorage
        //                .GetTransactionByIdAsync<BitcoinBasedTransaction>(currency.Name, txId)
        //                .ConfigureAwait(false);

        //            // request only not confirmed transactions
        //            if (localTx != null && localTx.IsConfirmed)
        //                continue;

        //            Log.Debug("Scan {@currency} transaction {@txId}", currency.Name, txId);

        //            var txResult = await currency.BlockchainApi
        //                .TryGetTransactionAsync(txId, cancellationToken: cancellationToken)
        //                .ConfigureAwait(false);

        //            if (txResult == null)
        //            {
        //                Log.Error("Error while get transactions {@txId}", txId);
        //                continue;
        //            }

        //            if (txResult.HasError)
        //            {
        //                Log.Error(
        //                    "Error while get transactions {@txId}. Code: {@code}. Description: {@desc}",
        //                    txId,
        //                    txResult.Error.Code,
        //                    txResult.Error.Description);

        //                continue;
        //            }

        //            var tx = txResult.Value;

        //            if (tx == null)
        //            {
        //                Log.Warning("Wow! Transaction with id {@txId} not found", txId);
        //                continue;
        //            }

        //            await _account
        //                .LocalStorage
        //                .UpsertTransactionAsync(
        //                    tx: tx,
        //                    notifyIfNewOrChanged: true,
        //                    cancellationToken: cancellationToken)
        //                .ConfigureAwait(false);
        //        }
        //    }
        //}
    }
}