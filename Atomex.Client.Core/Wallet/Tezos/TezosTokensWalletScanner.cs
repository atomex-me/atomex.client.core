﻿#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Atomex.Blockchain;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Wallets;
using Atomex.Wallets.Abstract;
using Atomex.Blockchain.Tezos.Common;

namespace Atomex.Wallet.Tezos
{
    public class TezosTokensWalletScanner : ICurrencyWalletScanner
    {
        private readonly TezosAccount _tezosAccount;
        private readonly ILogger? _logger;
        public string TokenType { get; }

        public TezosTokensWalletScanner(TezosAccount tezosAccount, string tokenType, ILogger? logger = null)
        {
            _tezosAccount = tezosAccount ?? throw new ArgumentNullException(nameof(tezosAccount));
            _logger = logger;
            TokenType = tokenType ?? throw new ArgumentNullException(nameof(tokenType));
        }

        public async Task ScanAsync(
            CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Start balance scan for all XTZ tokens addresses");

            // all tezos addresses
            var xtzAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(TezosConfig.Xtz, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (xtzAddresses.Count() <= 1)
            {
                // firstly scan xtz
                await new TezosWalletScanner(_tezosAccount, _logger)
                    .ScanAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            await UpdateBalanceAsync(skipUsed: false, cancellationToken)
                .ConfigureAwait(false);

            _logger?.LogInformation("Balance scan for all XTZ tokens addresses completed");
        }

        /// <summary>
        /// Update balances and transfers for all tokens for all accounts
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task UpdateBalanceAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Start balance update for all XTZ tokens addresses");

            // all tezos addresses
            var xtzLocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TezosHelper.Xtz,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // all tokens addresses
            var tokenLocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TokenType,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var uniqueLocalAddresses = xtzLocalAddresses
                .Concat(tokenLocalAddresses)
                .Select(w => w.Address)
                .Distinct();

            var tzktApi = new TzktApi(_tezosAccount.Config.GetTzktSettings());

            var (tokenBalances, error) = await tzktApi
                .GetTokenBalanceAsync(
                    addresses: uniqueLocalAddresses,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                _logger?.LogError("Error while scan tokens balance for all tokens and addresses. Code: {@code}. Message: {@message}",
                    error.Value.Code,
                    error.Value.Message);

                return;
            }

            if (tokenBalances == null)
            {
                _logger?.LogError("Error while scan tokens balance for all tokens and addresses. Token balances is null");
                return;
            }

            await ExtractAndSaveTokenContractsAsync(tokenBalances)
                .ConfigureAwait(false);

            var tokenBalanceMap = tokenBalances
                .ToDictionary(tb => UniqueTokenId(tb.Address, tb.Contract, tb.TokenId));

            var changedAddresses = new List<WalletAddress>();

            foreach (var tokenLocalAddress in tokenLocalAddresses)
            {
                var uniqueTokenId = UniqueTokenId(
                    tokenLocalAddress.Address,
                    tokenLocalAddress.TokenBalance.Contract,
                    tokenLocalAddress.TokenBalance.TokenId);

                if (tokenBalanceMap.TryGetValue(uniqueTokenId, out var tb))
                {
                    if (tokenLocalAddress.Balance != tb.GetTokenBalance() ||
                        tokenLocalAddress.TokenBalance.TransfersCount != tb.TransfersCount ||
                        tokenLocalAddress.TokenBalance.ArtifactUri != tb.ArtifactUri ||
                        tokenLocalAddress.TokenBalance.DisplayUri != tb.DisplayUri ||
                        tokenLocalAddress.TokenBalance.ThumbnailUri != tb.ThumbnailUri ||
                        tokenLocalAddress.TokenBalance.Name != tb.Name ||
                        tokenLocalAddress.TokenBalance.Description != tb.Description)
                    {
                        tokenLocalAddress.Balance = tb.GetTokenBalance();
                        tokenLocalAddress.TokenBalance = tb;

                        // save token local address to changed addresses list
                        changedAddresses.Add(tokenLocalAddress);
                    }

                    // remove token balance from map to track new tokens, than are not in the local database 
                    tokenBalanceMap.Remove(uniqueTokenId);
                }
                else // token balance at the address became zero
                {
                    if (tokenLocalAddress.Balance != 0)
                    {
                        tokenLocalAddress.Balance = 0;
                        tokenLocalAddress.TokenBalance.Balance = "0";

                        // save token local address to changed addresses list
                        changedAddresses.Add(tokenLocalAddress);
                    }
                }
            }

            // add new addresses with tokens
            var newTokenAddresses = tokenBalanceMap.Values
                .Select(tb =>
                {
                    var xtzAddress = xtzLocalAddresses.First(w => w.Address == tb.Address);

                    return new WalletAddress
                    {
                        Address      = tb.Address,
                        Balance      = tb.GetTokenBalance(),
                        Currency     = tb.ContractType,
                        KeyPath      = xtzAddress.KeyPath,
                        KeyIndex     = xtzAddress.KeyIndex,
                        KeyType      = xtzAddress.KeyType,
                        HasActivity  = true,
                        TokenBalance = tb
                    };
                });

            changedAddresses.AddRange(newTokenAddresses);

            // upsert changed token balances
            if (changedAddresses.Any())
            {
                await _tezosAccount
                    .LocalStorage
                    .UpsertAddressesAsync(changedAddresses, cancellationToken)
                    .ConfigureAwait(false);

                await UpdateAndSaveTokenTransfersAsync(
                        tzktApi,
                        changedAddresses: changedAddresses,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger?.LogInformation("Balance update for all XTZ tokens addresses completed");
        }

        /// <summary>
        /// Update balances and transfers for all tokens for specific address
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Start balance update for XTZ tokens address {@address}", address);

            // tezos address
            var xtzAddress = (await _tezosAccount.LocalStorage
                .GetAddressesAsync(TezosConfig.Xtz)
                .ConfigureAwait(false))
                .First(w => w.Address == address);

            // tezos tokens addresses
            var tokenLocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TokenType,
                    address: address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var tzktApi = new TzktApi(_tezosAccount.Config.GetTzktSettings());

            var (tokenBalances, error) = await tzktApi
                .GetTokenBalanceAsync(
                    addresses: new[] { address },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                _logger?.LogError("Error while scan tokens balance for all tokens and specific address {@address}. Code: {@code}. Message: {@message}",
                    address,
                    error.Value.Code,
                    error.Value.Message);

                return;
            }

            if (tokenBalances == null)
            {
                _logger?.LogError("Error while scan tokens balance for all tokens and specific address {@address}. Token balances is null",
                    address);
                return;
            }

            await ExtractAndSaveTokenContractsAsync(tokenBalances)
                .ConfigureAwait(false);

            var tokenBalanceMap = tokenBalances
                .ToDictionary(tb => UniqueTokenId(tb.Address, tb.Contract, tb.TokenId));

            var changedAddresses = new List<WalletAddress>();

            foreach (var tokenLocalAddress in tokenLocalAddresses)
            {
                var uniqueTokenId = UniqueTokenId(
                    tokenLocalAddress.Address,
                    tokenLocalAddress.TokenBalance.Contract,
                    tokenLocalAddress.TokenBalance.TokenId);

                if (tokenBalanceMap.TryGetValue(uniqueTokenId, out var tb))
                {
                    if (tokenLocalAddress.Balance != tb.GetTokenBalance() ||
                        tokenLocalAddress.TokenBalance.TransfersCount != tb.TransfersCount ||
                        tokenLocalAddress.TokenBalance.ArtifactUri != tb.ArtifactUri ||
                        tokenLocalAddress.TokenBalance.DisplayUri != tb.DisplayUri ||
                        tokenLocalAddress.TokenBalance.ThumbnailUri != tb.ThumbnailUri ||
                        tokenLocalAddress.TokenBalance.Name != tb.Name ||
                        tokenLocalAddress.TokenBalance.Description != tb.Description)
                    {
                        tokenLocalAddress.Balance = tb.GetTokenBalance();
                        tokenLocalAddress.TokenBalance = tb;

                        // save token local address to changed addresses list
                        changedAddresses.Add(tokenLocalAddress);
                    }

                    // remove token balance from map to track new tokens, than are not in the local database 
                    tokenBalanceMap.Remove(uniqueTokenId);
                }
                else // token balance at the address became zero
                {
                    if (tokenLocalAddress.Balance != 0)
                    {
                        tokenLocalAddress.Balance = 0;
                        tokenLocalAddress.TokenBalance.Balance = "0";

                        // save token local address to changed addresses list
                        changedAddresses.Add(tokenLocalAddress);
                    }
                }
            }

            // add new addresses with tokens
            var newTokenAddresses = tokenBalanceMap.Values
                .Select(tb =>
                {
                    return new WalletAddress
                    {
                        Address      = tb.Address,
                        Balance      = tb.GetTokenBalance(),
                        Currency     = tb.ContractType,
                        KeyPath      = xtzAddress.KeyPath,
                        KeyIndex     = xtzAddress.KeyIndex,
                        KeyType      = xtzAddress.KeyType,
                        HasActivity  = true,
                        TokenBalance = tb
                    };
                });

            changedAddresses.AddRange(newTokenAddresses);

            // upsert changed token balances
            if (changedAddresses.Any())
            {
                await _tezosAccount
                    .LocalStorage
                    .UpsertAddressesAsync(changedAddresses, cancellationToken)
                    .ConfigureAwait(false);

                await UpdateAndSaveTokenTransfersAsync(
                        tzktApi,
                        changedAddresses: changedAddresses,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger?.LogInformation("Balance update for XTZ tokens address {@addr} completed", address);
        }

        /// <summary>
        /// Update balances and transfers for specific token for all accounts
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task UpdateBalanceAsync(
            string tokenContract,
            BigInteger tokenId,
            CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Start balance update for XTZ token contract {@contract} and token id {@id}",
                tokenContract,
                tokenId.ToString());

            // all tezos addresses
            var xtzLocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(TezosConfig.Xtz, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // token's addresses
            // all tezos tokens addresses
            var tokenLocalAddresses = (await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TokenType,
                    tokenContract: tokenContract,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .Where(w =>
                    w.TokenBalance.Contract == tokenContract &&
                    w.TokenBalance.TokenId == tokenId)
                .ToList();

            var uniqueLocalAddresses = xtzLocalAddresses
                .Concat(tokenLocalAddresses)
                .Select(w => w.Address)
                .Distinct();

            var tzktApi = new TzktApi(_tezosAccount.Config.GetTzktSettings());

            var (tokenBalances, error) = await tzktApi
                .GetTokenBalanceAsync(
                    addresses: uniqueLocalAddresses,
                    tokenContracts: new [] { tokenContract },
                    tokenIds: new [] { tokenId },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                _logger?.LogError("Error while scan tokens balance for specific token contract {@contract} and token id {@id} for all addresses. Code: {@code}. Message: {@message}",
                    tokenContract,
                    tokenId.ToString(),
                    error.Value.Code,
                    error.Value.Message);

                return;
            }

            if (tokenBalances == null)
            {
                _logger?.LogError("Error while scan tokens balance for specific token contract {@contract} and token id {@id} for all addresses. Token balances is null",
                    tokenContract,
                    tokenId.ToString());
                return;
            }

            await ExtractAndSaveTokenContractsAsync(tokenBalances)
                .ConfigureAwait(false);

            var tokenBalanceMap = tokenBalances
                .ToDictionary(tb => UniqueTokenId(tb.Address, tb.Contract, tb.TokenId));

            var changedAddresses = new List<WalletAddress>();

            foreach (var tokenLocalAddress in tokenLocalAddresses)
            {
                var uniqueTokenId = UniqueTokenId(
                    tokenLocalAddress.Address,
                    tokenLocalAddress.TokenBalance.Contract,
                    tokenLocalAddress.TokenBalance.TokenId);

                if (tokenBalanceMap.TryGetValue(uniqueTokenId, out var tb))
                {
                    if (tokenLocalAddress.Balance != tb.GetTokenBalance() ||
                        tokenLocalAddress.TokenBalance.TransfersCount != tb.TransfersCount ||
                        tokenLocalAddress.TokenBalance.ArtifactUri != tb.ArtifactUri ||
                        tokenLocalAddress.TokenBalance.DisplayUri != tb.DisplayUri ||
                        tokenLocalAddress.TokenBalance.ThumbnailUri != tb.ThumbnailUri ||
                        tokenLocalAddress.TokenBalance.Name != tb.Name ||
                        tokenLocalAddress.TokenBalance.Description != tb.Description)
                    {
                        tokenLocalAddress.Balance = tb.GetTokenBalance();
                        tokenLocalAddress.TokenBalance = tb;

                        // save token local address to changed addresses list
                        changedAddresses.Add(tokenLocalAddress);
                    }

                    // remove token balance from map to track new tokens, than are not in the local database 
                    tokenBalanceMap.Remove(uniqueTokenId);
                }
                else // token balance at the address became zero
                {
                    if (tokenLocalAddress.Balance != 0)
                    {
                        tokenLocalAddress.Balance = 0;
                        tokenLocalAddress.TokenBalance.Balance = "0";

                        // save token local address to changed addresses list
                        changedAddresses.Add(tokenLocalAddress);
                    }
                }
            }

            // add new addresses with tokens
            var newTokenAddresses = tokenBalanceMap.Values
                .Select(tb =>
                {
                    var xtzAddress = xtzLocalAddresses.First(w => w.Address == tb.Address);

                    return new WalletAddress
                    {
                        Address      = tb.Address,
                        Balance      = tb.GetTokenBalance(),
                        Currency     = tb.ContractType,
                        KeyPath      = xtzAddress.KeyPath,
                        KeyIndex     = xtzAddress.KeyIndex,
                        KeyType      = xtzAddress.KeyType,
                        HasActivity  = true,
                        TokenBalance = tb
                    };
                });

            changedAddresses.AddRange(newTokenAddresses);

            // upsert changed token balances
            if (changedAddresses.Any())
            {
                await _tezosAccount
                    .LocalStorage
                    .UpsertAddressesAsync(changedAddresses, cancellationToken)
                    .ConfigureAwait(false);

                await UpdateAndSaveTokenTransfersAsync(
                        tzktApi,
                        changedAddresses: changedAddresses,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger?.LogInformation("Balance update for XTZ token contract {@contract} and token id {@id} completed",
                tokenContract,
                tokenId.ToString());
        }

        /// <summary>
        /// Update balances and transfers for specific token and address
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task UpdateBalanceAsync(
            string address,
            string tokenContract,
            BigInteger tokenId,
            CancellationToken cancellationToken = default)
        {
            _logger?.LogInformation("Start balance update for XTZ token contract {@contract} and token id {@id} for address {@addr}",
                tokenContract,
                tokenId.ToString(),
                address);

            // tezos address
            var xtzAddress = (await _tezosAccount.LocalStorage
                .GetAddressesAsync(TezosConfig.Xtz)
                .ConfigureAwait(false))
                .First(w => w.Address == address);

            // tezos token address
            var tokenLocalAddresses = (await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TokenType,
                    tokenContract: tokenContract,
                    address: address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .Where(w => w.TokenBalance.TokenId == tokenId)
                .ToList();

            var tzktApi = new TzktApi(_tezosAccount.Config.GetTzktSettings());

            var (tokenBalances, error) = await tzktApi
                .GetTokenBalanceAsync(
                    addresses: new [] { address },
                    tokenContracts: new [] { tokenContract },
                    tokenIds: new [] { tokenId },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                _logger?.LogError("Error while scan tokens balance for specific token contract {@contract} and token id {@id} and address {@addr}. Code: {@code}. Message: {@message}",
                    tokenContract,
                    tokenId.ToString(),
                    address,
                    error.Value.Code,
                    error.Value.Message);

                return;
            }

            if (tokenBalances == null)
            {
                _logger?.LogError("Error while scan tokens balance for specific token contract {@contract} and token id {@id} and address {@addr}. Token balances is null",
                    tokenContract,
                    tokenId.ToString(),
                    address);
                return;
            }

            await ExtractAndSaveTokenContractsAsync(tokenBalances)
                .ConfigureAwait(false);

            var tokenBalanceMap = tokenBalances
                .ToDictionary(tb => UniqueTokenId(tb.Address, tb.Contract, tb.TokenId));

            var changedAddresses = new List<WalletAddress>();

            foreach (var tokenLocalAddress in tokenLocalAddresses)
            {
                var uniqueTokenId = UniqueTokenId(
                    tokenLocalAddress.Address,
                    tokenLocalAddress.TokenBalance.Contract,
                    tokenLocalAddress.TokenBalance.TokenId);

                if (tokenBalanceMap.TryGetValue(uniqueTokenId, out var tb))
                {
                    if (tokenLocalAddress.Balance != tb.GetTokenBalance() ||
                        tokenLocalAddress.TokenBalance.TransfersCount != tb.TransfersCount ||
                        tokenLocalAddress.TokenBalance.ArtifactUri != tb.ArtifactUri ||
                        tokenLocalAddress.TokenBalance.DisplayUri != tb.DisplayUri ||
                        tokenLocalAddress.TokenBalance.ThumbnailUri != tb.ThumbnailUri ||
                        tokenLocalAddress.TokenBalance.Name != tb.Name ||
                        tokenLocalAddress.TokenBalance.Description != tb.Description)
                    {
                        tokenLocalAddress.Balance = tb.GetTokenBalance();
                        tokenLocalAddress.TokenBalance = tb;

                        // save token local address to changed addresses list
                        changedAddresses.Add(tokenLocalAddress);
                    }

                    // remove token balance from map to track new tokens, than are not in the local database 
                    tokenBalanceMap.Remove(uniqueTokenId);
                }
                else // token balance at the address became zero
                {
                    if (tokenLocalAddress.Balance != 0)
                    {
                        tokenLocalAddress.Balance = 0;
                        tokenLocalAddress.TokenBalance.Balance = "0";

                        // save token local address to changed addresses list
                        changedAddresses.Add(tokenLocalAddress);
                    }
                }
            }

            // add new addresses with tokens
            var newTokenAddresses = tokenBalanceMap.Values
                .Select(tb =>
                {
                    return new WalletAddress
                    {
                        Address      = tb.Address,
                        Balance      = tb.GetTokenBalance(),
                        Currency     = tb.ContractType,
                        KeyPath      = xtzAddress.KeyPath,
                        KeyIndex     = xtzAddress.KeyIndex,
                        KeyType      = xtzAddress.KeyType,
                        HasActivity  = true,
                        TokenBalance = tb
                    };
                });

            changedAddresses.AddRange(newTokenAddresses);

            // upsert changed token balances
            if (changedAddresses.Any())
            {
                await _tezosAccount
                    .LocalStorage
                    .UpsertAddressesAsync(changedAddresses, cancellationToken)
                    .ConfigureAwait(false);

                await UpdateAndSaveTokenTransfersAsync(
                        tzktApi,
                        changedAddresses: changedAddresses,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger?.LogInformation("Balance update for XTZ token contract {@contract} and token id {@id} for address {@addr} completed",
                tokenContract,
                tokenId.ToString(),
                address);
        }

        private static string UniqueTokenId(string address, string contract, BigInteger tokenId) =>
            $"{address}:{contract}:{tokenId}";

        private async Task ExtractAndSaveTokenContractsAsync(
            IEnumerable<TokenBalance> tokenBalances)
        {
            var tokenContracts = tokenBalances
                .GroupBy(tb => tb.Contract)
                .Select(g => new TokenContract
                {
                    Address = g.First().Contract,
                    Name = g.First().ContractAlias,
                    Type = g.First().ContractType
                });

            if (tokenContracts.Any())
            {
                await _tezosAccount
                    .LocalStorage
                    .UpsertTokenContractsAsync(tokenContracts)
                    .ConfigureAwait(false);
            }
        }

        private async Task UpdateAndSaveTokenTransfersAsync(
            TzktApi tzktApi,
            IEnumerable<WalletAddress> changedAddresses,
            CancellationToken cancellationToken = default)
        {
            var addresses = changedAddresses.Select(w => w.Address)
                .Distinct()
                .ToList();

            var contracts = changedAddresses
                .Select(w => w.TokenBalance.Contract)
                .Distinct()
                .ToList();

            var tokenIds = changedAddresses
                .Select(w => w.TokenBalance.TokenId)
                .Distinct()
                .ToList();

            // scan transfers if need
            var (transfers, error) = await tzktApi
                .GetTokenTransfersAsync(
                    addresses: addresses,
                    tokenContracts: contracts,
                    tokenIds: tokenIds,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                _logger?.LogError("Error while get transfers. Code: {@code}. Message: {@message}",
                    error.Value.Code,
                    error.Value.Message);

                return;
            }

            if (transfers?.Any() ?? false)
            {
                await _tezosAccount
                    .LocalStorage
                    .UpsertTransactionsAsync(transfers, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}