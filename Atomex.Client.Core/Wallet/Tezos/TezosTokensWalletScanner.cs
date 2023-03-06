using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Core;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet.Tezos
{
    public class TezosTokensWalletScanner : ICurrencyWalletScanner
    {
        private readonly TezosAccount _tezosAccount;

        public TezosTokensWalletScanner(TezosAccount tezosAccount)
        {
            _tezosAccount = tezosAccount ?? throw new ArgumentNullException(nameof(tezosAccount));
        }

        public async Task ScanAsync(
            CancellationToken cancellationToken = default)
        {
            // all tezos addresses
            var xtzAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(TezosConfig.Xtz, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (xtzAddresses.Count() <= 1)
            {
                // firstly scan xtz
                await new TezosWalletScanner(_tezosAccount)
                    .ScanAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            await UpdateBalanceAsync(skipUsed: false, cancellationToken)
                .ConfigureAwait(false);
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
        public async Task UpdateBalanceAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            // all tezos addresses
            var xtzLocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TezosHelper.Xtz,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // all tezos tokens addresses
            var fa12LocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TezosHelper.Fa12,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var fa2LocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TezosHelper.Fa2,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var tokenLocalAddresses = fa12LocalAddresses
                .Concat(fa2LocalAddresses);

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
                Log.Error("Error while scan tokens balance for all tokens and addresses. Code: {@code}. Message: {@message}",
                    error.Value.Code,
                    error.Value.Message);

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
            // all tezos addresses
            var xtzLocalAddresses = await _tezosAccount.LocalStorage
                .GetAddressesAsync(TezosConfig.Xtz)
                .ConfigureAwait(false);

            var xtzAddress = xtzLocalAddresses.First(w => w.Address == address);

            // tezos tokens addresses
            var fa12LocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TezosHelper.Fa12,
                    address: address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var fa2LocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TezosHelper.Fa2,
                    address: address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var tokenLocalAddresses = fa12LocalAddresses
                .Concat(fa2LocalAddresses);

            var tzktApi = new TzktApi(_tezosAccount.Config.GetTzktSettings());

            var (tokenBalances, error) = await tzktApi
                .GetTokenBalanceAsync(
                    addresses: new[] { address },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error("Error while scan tokens balance for all tokens and specific address. Code: {@code}. Message: {@message}",
                    error.Value.Code,
                    error.Value.Message);

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
            // all tezos addresses
            var xtzLocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(TezosConfig.Xtz, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // token's addresses
            // all tezos tokens addresses
            var fa12LocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TezosHelper.Fa12,
                    tokenContract: tokenContract,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var fa2LocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TezosHelper.Fa2,
                    tokenContract: tokenContract,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var tokenLocalAddresses = fa12LocalAddresses
                .Concat(fa2LocalAddresses)
                .Where(w =>
                    w.TokenBalance.Contract == tokenContract &&
                    w.TokenBalance.TokenId == tokenId);

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
                Log.Error("Error while scan tokens balance for specific token and all addresses. Code: {@code}. Message: {@message}",
                    error.Value.Code,
                    error.Value.Message);

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
            // all tezos addresses
            var xtzLocalAddresses = await _tezosAccount.LocalStorage
                .GetAddressesAsync(TezosConfig.Xtz)
                .ConfigureAwait(false);

            var xtzAddress = xtzLocalAddresses.First(w => w.Address == address);

            // tezos token address
            var fa12LocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TezosHelper.Fa12,
                    tokenContract: tokenContract,
                    address: address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var fa2LocalAddresses = await _tezosAccount
                .LocalStorage
                .GetAddressesAsync(
                    currency: TezosHelper.Fa2,
                    tokenContract: tokenContract,
                    address: address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var tokenLocalAddresses = fa12LocalAddresses
                .Concat(fa2LocalAddresses)
                .Where(w => w.TokenBalance.TokenId == tokenId);

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
                Log.Error("Error while scan tokens balance for specific token and address. Code: {@code}. Message: {@message}",
                    error.Value.Code,
                    error.Value.Message);

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
                Log.Error("Error while get transfers. Code: {@code}. Message: {@message}",
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