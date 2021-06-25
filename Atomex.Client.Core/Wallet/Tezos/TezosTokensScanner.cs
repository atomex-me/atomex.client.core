﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Tezos;
using Atomex.Core;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Tezos
{
    public class TezosTokensScanner : ICurrencyHdWalletScanner
    {
        private readonly TezosAccount _tezosAccount;

        public TezosTokensScanner(TezosAccount tezosAccount)
        {
            _tezosAccount = tezosAccount ?? throw new ArgumentNullException(nameof(tezosAccount));
        }

        public Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                // addresses from local db
                var localAddresses = await _tezosAccount.DataRepository
                    .GetTezosTokenAddressesAsync()
                    .ConfigureAwait(false);

                // all tezos addresses
                var xtzAddresses = await _tezosAccount.DataRepository
                    .GetAddressesAsync(TezosConfig.Xtz)
                    .ConfigureAwait(false);

                var addresses = localAddresses
                    .Select(a => a.Address)
                    .ToList();

                if (xtzAddresses.Any())
                    addresses.AddRange(xtzAddresses.Select(a => a.Address));

                // remove duplicates
                addresses = addresses
                    .Distinct()
                    .ToList();

                foreach (var address in addresses)
                {
                    await ScanAsync(address, cancellationToken)
                        .ConfigureAwait(false);
                }

            }, cancellationToken);
        }

        public Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run((async () =>
            {
                var tezosConfig = _tezosAccount.Config;

                var bcdSettings = tezosConfig.BcdApiSettings;

                var bcdApi = new BcdApi(bcdSettings);

                var tokenContractsResult = await bcdApi
                    .GetTokenContractsAsync(address, cancellationToken)
                    .ConfigureAwait(false);

                if (tokenContractsResult.HasError)
                {
                    Log.Error($"Error while get token contracts for " +
                        $"address: {address}. " +
                        $"Code: {tokenContractsResult.Error.Code}. " +
                        $"Description: {tokenContractsResult.Error.Description}.");

                    return;
                }

                // contracts from local db
                var contracts = (await _tezosAccount.DataRepository
                    .GetTezosTokenAddressesAsync(address)
                    .ConfigureAwait(false))
                    .Select(a => a.TokenBalance.Contract)
                    .ToList();

                // add contracts from network
                if (tokenContractsResult.Value.Any())
                    contracts.AddRange(tokenContractsResult.Value.Keys);

                contracts = contracts
                    .Distinct()
                    .ToList();

                // scan by address and contract
                foreach (var contractAddress in contracts)
                {
                    var contractWithMetadata = tokenContractsResult.Value.TryGetValue(contractAddress, out var contract)
                        ? contract
                        : null;

                    await ScanContractAsync(
                            address,
                            contractAddress,
                            contractWithMetadata,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

            }), cancellationToken);
        }

        public Task ScanContractAsync(
            string contractAddress,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                // addresses from local db
                var localAddresses = await _tezosAccount.DataRepository
                    .GetTezosTokenAddressesByContractAsync(contractAddress)
                    .ConfigureAwait(false);

                // all tezos addresses
                var xtzAddresses = await _tezosAccount.DataRepository
                    .GetAddressesAsync(TezosConfig.Xtz)
                    .ConfigureAwait(false);

                var addresses = localAddresses
                    .Select(a => a.Address)
                    .ToList();

                if (xtzAddresses.Any())
                    addresses.AddRange(xtzAddresses.Select(a => a.Address));

                // remove duplicates
                addresses = addresses
                    .Distinct()
                    .ToList();

                foreach (var address in addresses)
                {
                    await ScanContractAsync(
                            address,
                            contractAddress,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

            }, cancellationToken);
        }

        public Task ScanContractAsync(
            string address,
            string contractAddress,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var tezosConfig = _tezosAccount.Config;

                var bcdSettings = tezosConfig.BcdApiSettings;

                var bcdApi = new BcdApi(bcdSettings);

                var tokenContractsResult = await bcdApi
                    .GetTokenContractsAsync(address, cancellationToken)
                    .ConfigureAwait(false);

                if (tokenContractsResult.HasError)
                {
                    Log.Error($"Error while get token contracts for " +
                        $"address: {address}. " +
                        $"Code: {tokenContractsResult.Error.Code}. " +
                        $"Description: {tokenContractsResult.Error.Description}.");

                    return;
                }

                var contractWithMetadata = tokenContractsResult.Value.TryGetValue(contractAddress, out var contract)
                    ? contract
                    : null;

                await ScanContractAsync(
                        address,
                        contractAddress,
                        contractWithMetadata,
                        cancellationToken)
                    .ConfigureAwait(false);

            }, cancellationToken);
        }
    
        private Task ScanContractAsync(
            string address,
            string contractAddress,
            TokenContractWithMetadata contract,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var tezosConfig = _tezosAccount.Config;

                var bcdSettings = tezosConfig.BcdApiSettings;

                var bcdApi = new BcdApi(bcdSettings);

                var contractType = contract
                    ?.GetContractType() ?? "";



                var tokenBalancesResult = await bcdApi
                    .GetTokenBalancesAsync(address, contractAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (tokenBalancesResult.HasError)
                {
                    Log.Error("Error while scan tokens balance for " +
                        $"contract: {contractAddress} and " +
                        $"address: {address}. " +
                        $"Code: {tokenBalancesResult.Error.Code}. " +
                        $"Description: {tokenBalancesResult.Error.Description}");

                    return;
                }

                var localTokenAddresses = (await _tezosAccount.DataRepository
                    .GetTezosTokenAddressesAsync(address, contractAddress)
                    .ConfigureAwait(false))
                    .ToList();

                var tokenBalanceDict = tokenBalancesResult.Value
                    .ToDictionary(
                        tb => UniqueTokenId(contractType, tb.Contract, tb.TokenId),
                        tb => tb);

                foreach (var localAddress in localTokenAddresses)
                {
                    var uniqueTokenId = UniqueTokenId(
                        localAddress.Currency,
                        localAddress.TokenBalance.Contract,
                        localAddress.TokenBalance.TokenId);

                    if (tokenBalanceDict.TryGetValue(uniqueTokenId, out var tb))
                    {
                        localAddress.Balance = tb.GetTokenBalance();

                        tokenBalanceDict.Remove(uniqueTokenId);
                    }
                    else
                    {
                        localAddress.Balance = 0;
                        // todo: may be remove zero address from db?
                    }
                }

                var xtzAddress = await _tezosAccount
                    .GetAddressAsync(address, cancellationToken)
                    .ConfigureAwait(false);

                var newTokenAddresses = tokenBalanceDict.Values
                    .Select(tb => new WalletAddress
                    {
                        Address      = address,
                        Balance      = tb.GetTokenBalance(),
                        Currency     = contractType,
                        KeyIndex     = xtzAddress.KeyIndex,
                        HasActivity  = true,
                        TokenBalance = tb
                    });

                localTokenAddresses.AddRange(newTokenAddresses);

                if (localTokenAddresses.Any())
                    await _tezosAccount.DataRepository
                        .UpsertTezosTokenAddressesAsync(localTokenAddresses)
                        .ConfigureAwait(false);

                // scan transfers

                foreach (var localAddress in localTokenAddresses)
                {
                    var transfersResult = await bcdApi
                        .GetTokenTransfers(
                            address: localAddress.Address,
                            contractAddress: localAddress.TokenBalance.Contract,
                            tokenId: localAddress.TokenBalance.TokenId,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (transfersResult.HasError)
                    {
                        Log.Error($"Error while get transfers for " +
                            $"address: {localAddress.Address}, " +
                            $"contract: {localAddress.TokenBalance.Contract} and " +
                            $"token id: {localAddress.TokenBalance.TokenId}. " +
                            $"Code: {transfersResult.Error.Code}. " +
                            $"Description: {transfersResult.Error.Description}.");

                        return;
                    }

                    transfersResult.Value.ForEach(t => t.Currency = contractType);

                    if (transfersResult?.Value?.Any() ?? false)
                        await _tezosAccount.DataRepository
                            .UpsertTezosTokenTransfersAsync(transfersResult.Value)
                            .ConfigureAwait(false);
                }

            }, cancellationToken);
        }

        private static string UniqueTokenId(string type, string contract, decimal tokenId) =>
            $"{type}:{contract}:{tokenId}";
    }
}