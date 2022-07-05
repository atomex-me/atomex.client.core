using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Tzkt;
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
                // all tezos addresses
                var xtzAddresses = await _tezosAccount.DataRepository
                    .GetAddressesAsync(TezosConfig.Xtz)
                    .ConfigureAwait(false);

                if (xtzAddresses.Count() <= 1)
                {
                    // firstly scan xtz
                    await new TezosWalletScanner(_tezosAccount)
                        .ScanAsync(cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                await UpdateBalanceAsync(cancellationToken)
                    .ConfigureAwait(false);

            }, cancellationToken);
        }

        public Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return UpdateBalanceAsync(address, cancellationToken);
        }

        /// <summary>
        /// Update balances and transfers for all tokens for all accounts
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                // all tezos addresses
                var xtzLocalAddresses = await _tezosAccount.DataRepository
                    .GetAddressesAsync(TezosConfig.Xtz)
                    .ConfigureAwait(false);

                // all tezos tokens addresses
                var tokenLocalAddresses = await _tezosAccount.DataRepository
                    .GetTezosTokenAddressesAsync()
                    .ConfigureAwait(false);

                var uniqueLocalAddresses = xtzLocalAddresses
                    .Concat(tokenLocalAddresses)
                    .Select(w => w.Address)
                    .Distinct();

                var tzktApi = new TzktApi(_tezosAccount.Config);

                var tokenBalancesResult = await tzktApi
                    .GetTokenBalanceAsync(
                        addresses: uniqueLocalAddresses,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (tokenBalancesResult.HasError)
                {
                    Log.Error("Error while scan tokens balance for all tokens and addresses. Code: {@code}. Description: {@descr}",
                        tokenBalancesResult.Error.Code,
                        tokenBalancesResult.Error.Description);

                    return;
                }

                await ExtractAndSaveTokenContractsAsync(tokenBalancesResult.Value)
                    .ConfigureAwait(false);

                var tokenBalanceMap = tokenBalancesResult.Value
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
                            tokenLocalAddress.TokenBalance.TransfersCount != tb.TransfersCount)
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
                    await _tezosAccount.DataRepository
                        .UpsertTezosTokenAddressesAsync(changedAddresses)
                        .ConfigureAwait(false);

                    await UpdateAndSaveTokenTransfersAsync(
                            tzktApi,
                            changedAddresses: changedAddresses.Select(w => w.Address),
                            allAddresses: uniqueLocalAddresses
                                .Concat(newTokenAddresses.Select(w => w.Address))
                                .Distinct(),
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                // raise balance updated event for all tokens and accounts
                _tezosAccount.RaiseTokensBalanceUpdatedEvent(
                    address: null,
                    tokenContract: null,
                    tokenId: null);

            }, cancellationToken);
        }

        /// <summary>
        /// Update balances and transfers for all tokens for specific address
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                // all tezos addresses
                var xtzLocalAddresses = await _tezosAccount.DataRepository
                    .GetAddressesAsync(TezosConfig.Xtz)
                    .ConfigureAwait(false);

                var xtzAddress = xtzLocalAddresses.First(w => w.Address == address);

                // tezos tokens addresses
                var tokenLocalAddresses = (await _tezosAccount.DataRepository
                    .GetTezosTokenAddressesAsync()
                    .ConfigureAwait(false))
                    .Where(w => w.Address == address);

                var tzktApi = new TzktApi(_tezosAccount.Config);

                var tokenBalancesResult = await tzktApi
                    .GetTokenBalanceAsync(
                        addresses: new[] { address },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (tokenBalancesResult.HasError)
                {
                    Log.Error("Error while scan tokens balance for all tokens and specific address. Code: {@code}. Description: {@descr}",
                        tokenBalancesResult.Error.Code,
                        tokenBalancesResult.Error.Description);

                    return;
                }

                await ExtractAndSaveTokenContractsAsync(tokenBalancesResult.Value)
                    .ConfigureAwait(false);

                var tokenBalanceMap = tokenBalancesResult.Value
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
                            tokenLocalAddress.TokenBalance.TransfersCount != tb.TransfersCount)
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
                    await _tezosAccount.DataRepository
                        .UpsertTezosTokenAddressesAsync(changedAddresses)
                        .ConfigureAwait(false);

                    await UpdateAndSaveTokenTransfersAsync(
                            tzktApi,
                            changedAddresses: changedAddresses.Select(w => w.Address),
                            allAddresses: xtzLocalAddresses.Select(w => w.Address)
                                .Concat(newTokenAddresses.Select(w => w.Address))
                                .Distinct(),
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                // raise balance updated event for all tokens for specific account
                _tezosAccount.RaiseTokensBalanceUpdatedEvent(
                    address: address,
                    tokenContract: null,
                    tokenId: null);

            }, cancellationToken);
        }

        /// <summary>
        /// Update balances and transfers for specific token for all accounts
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task UpdateBalanceAsync(
            string tokenContract,
            int tokenId,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                // all tezos addresses
                var xtzLocalAddresses = await _tezosAccount.DataRepository
                    .GetAddressesAsync(TezosConfig.Xtz)
                    .ConfigureAwait(false);

                // token's addresses
                var allTokensLocalAddresses = await _tezosAccount.DataRepository
                    .GetTezosTokenAddressesAsync()
                    .ConfigureAwait(false);

                var tokenLocalAddresses = allTokensLocalAddresses.Where(w =>
                    w.TokenBalance.Contract == tokenContract &&
                    w.TokenBalance.TokenId == tokenId);

                var uniqueLocalAddresses = xtzLocalAddresses
                    .Concat(tokenLocalAddresses)
                    .Select(w => w.Address)
                    .Distinct();

                var tzktApi = new TzktApi(_tezosAccount.Config);

                var tokenBalancesResult = await tzktApi
                    .GetTokenBalanceAsync(
                        addresses: uniqueLocalAddresses,
                        tokenContracts: new [] { tokenContract },
                        tokenIds: new [] { tokenId },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (tokenBalancesResult.HasError)
                {
                    Log.Error("Error while scan tokens balance for specific token and all addresses. Code: {@code}. Description: {@descr}",
                        tokenBalancesResult.Error.Code,
                        tokenBalancesResult.Error.Description);

                    return;
                }

                await ExtractAndSaveTokenContractsAsync(tokenBalancesResult.Value)
                    .ConfigureAwait(false);

                var tokenBalanceMap = tokenBalancesResult.Value
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
                            tokenLocalAddress.TokenBalance.TransfersCount != tb.TransfersCount)
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
                            Address = tb.Address,
                            Balance = tb.GetTokenBalance(),
                            Currency = tb.ContractType,
                            KeyIndex = xtzAddress.KeyIndex,
                            KeyType = xtzAddress.KeyType,
                            HasActivity = true,
                            TokenBalance = tb
                        };
                    });

                changedAddresses.AddRange(newTokenAddresses);

                // upsert changed token balances
                if (changedAddresses.Any())
                {
                    await _tezosAccount.DataRepository
                        .UpsertTezosTokenAddressesAsync(changedAddresses)
                        .ConfigureAwait(false);

                    await UpdateAndSaveTokenTransfersAsync(
                            tzktApi,
                            changedAddresses: changedAddresses.Select(w => w.Address),
                            allAddresses: xtzLocalAddresses.Select(w => w.Address)
                                .Concat(allTokensLocalAddresses.Select(w => w.Address))
                                .Concat(newTokenAddresses.Select(w => w.Address))
                                .Distinct(),
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                // raise balance updated event for specific tokens and all accounts
                _tezosAccount.RaiseTokensBalanceUpdatedEvent(
                    address: null,
                    tokenContract: tokenContract,
                    tokenId: tokenId);

            }, cancellationToken);
        }

        /// <summary>
        /// Update balances and transfers for specific token and address
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task UpdateBalanceAsync(
            string address,
            string tokenContract,
            int tokenId,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                // all tezos addresses
                var xtzLocalAddresses = await _tezosAccount.DataRepository
                    .GetAddressesAsync(TezosConfig.Xtz)
                    .ConfigureAwait(false);

                var xtzAddress = xtzLocalAddresses.First(w => w.Address == address);

                // tezos token address
                var tokenLocalAddresses = (await _tezosAccount.DataRepository
                    .GetTezosTokenAddressesAsync(address, tokenContract)
                    .ConfigureAwait(false))
                    .Where(w => w.TokenBalance.TokenId == tokenId);

                var tzktApi = new TzktApi(_tezosAccount.Config);

                var tokenBalancesResult = await tzktApi
                    .GetTokenBalanceAsync(
                        addresses: new [] { address },
                        tokenContracts: new [] { tokenContract },
                        tokenIds: new [] { tokenId },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (tokenBalancesResult.HasError)
                {
                    Log.Error("Error while scan tokens balance for specific token and address. Code: {@code}. Description: {@descr}",
                        tokenBalancesResult.Error.Code,
                        tokenBalancesResult.Error.Description);

                    return;
                }

                await ExtractAndSaveTokenContractsAsync(tokenBalancesResult.Value)
                    .ConfigureAwait(false);

                var tokenBalanceMap = tokenBalancesResult.Value
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
                            tokenLocalAddress.TokenBalance.TransfersCount != tb.TransfersCount)
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
                    await _tezosAccount.DataRepository
                        .UpsertTezosTokenAddressesAsync(changedAddresses)
                        .ConfigureAwait(false);

                    await UpdateAndSaveTokenTransfersAsync(
                            tzktApi,
                            changedAddresses: changedAddresses.Select(w => w.Address),
                            allAddresses: xtzLocalAddresses.Select(w => w.Address)
                                .Concat(newTokenAddresses.Select(w => w.Address))
                                .Distinct(),
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                // raise balance updated event for all tokens and accounts
                _tezosAccount.RaiseTokensBalanceUpdatedEvent(
                    address: address,
                    tokenContract: tokenContract,
                    tokenId: tokenId);

            }, cancellationToken);
        }

        private static string UniqueTokenId(string address, string contract, decimal tokenId) =>
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
                await _tezosAccount.DataRepository
                    .UpsertTezosTokenContractsAsync(tokenContracts)
                    .ConfigureAwait(false);
            }
        }

        private async Task UpdateAndSaveTokenTransfersAsync(
            TzktApi tzktApi,
            IEnumerable<string> changedAddresses,
            IEnumerable<string> allAddresses,
            CancellationToken cancellationToken = default)
        {
            // scan transfers if need
            var transfersResult = await tzktApi
                .GetTokenTransfersAsync(
                    addresses: changedAddresses,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (transfersResult.HasError)
            {
                Log.Error("Error while get transfers. Code: {@code}. Description: {@descr}.",
                    transfersResult.Error.Code,
                    transfersResult.Error.Description);

                return;
            }

            if (transfersResult.Value?.Any() ?? false)
            {
                var localAddressesHashSet = allAddresses.ToHashSet();

                // resolve transfers type
                foreach (var transfer in transfersResult.Value)
                {
                    if (localAddressesHashSet.Contains(transfer.From))
                        transfer.Type |= BlockchainTransactionType.Output;
                    if (localAddressesHashSet.Contains(transfer.To))
                        transfer.Type |= BlockchainTransactionType.Input;
                }

                await _tezosAccount.DataRepository
                    .UpsertTezosTokenTransfersAsync(transfersResult.Value)
                    .ConfigureAwait(false);
            }
        }
    }
}