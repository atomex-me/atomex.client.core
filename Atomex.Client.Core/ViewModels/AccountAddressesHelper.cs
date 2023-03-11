using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Core;
using Atomex.Wallet.Abstract;

namespace Atomex.ViewModels
{
    public static class AccountAddressesHelper
    {
        public static async Task<IEnumerable<WalletAddressViewModel>> GetReceivingAddressesAsync(
            IAccount account,
            ILocalStorage localStorage,
            CurrencyConfig currency,
            string tokenType = null,
            string tokenContract = null,
            int tokenId = 0)
        {
            var isToken = Currencies.IsToken(currency.Name) || tokenContract != null;

            if (isToken)
            {
                if (currency is ITokenConfig tokenConfig)
                {
                    tokenContract ??= tokenConfig.TokenContractAddress;
                    tokenType ??= tokenConfig.Standard;
                }
                else
                {
                    tokenConfig = account
                        .Currencies
                        .FirstOrDefault(c => c is ITokenConfig t &&
                                             t.TokenContractAddress == tokenContract &&
                                             t.TokenId == tokenId) as ITokenConfig;
                }

                if (tokenContract == null)
                    return Enumerable.Empty<WalletAddressViewModel>();

                var baseChainName = tokenConfig?.BaseCurrencyName ?? currency.Name;

                var baseChainAccount = account
                    .GetCurrencyAccount(baseChainName);

                var unspentBaseChainAddresses = await baseChainAccount
                    .GetUnspentAddressesAsync()
                    .ConfigureAwait(false);

                var freeBaseChainAddress = await baseChainAccount
                    .GetFreeExternalAddressAsync()
                    .ConfigureAwait(false);

                var baseChainAddresses = unspentBaseChainAddresses
                    .Concat(new[] { freeBaseChainAddress });

                var tokenAddresses = (await localStorage
                    .GetAddressesAsync(tokenType, tokenContract: tokenContract)
                    .ConfigureAwait(false))
                    .Where(wa => wa.TokenBalance?.TokenId == tokenId)
                    .ToList();

                var baseChainAddressesWithoutTokens = baseChainAddresses
                    .Where(w => tokenAddresses.All(ta => ta.Address != w.Address))
                    .ToList();

                return tokenAddresses
                    .Concat(baseChainAddressesWithoutTokens)
                    .Select(w =>
                    {
                        WalletAddress baseChainAddress, tokenAddress;

                        if (w.Currency == baseChainName)
                        {
                            baseChainAddress = w;
                            tokenAddress = new WalletAddress
                            {
                                Address     = w.Address,
                                Currency    = tokenType,
                                Balance     = 0,
                                HasActivity = false,
                                KeyIndex    = w.KeyIndex,
                                KeyType     = w.KeyType,
                                TokenBalance = new TokenBalance
                                {
                                    Contract = tokenContract,
                                    Balance  = "0",
                                    Symbol   = tokenConfig?.DisplayedName ?? string.Empty,
                                    Decimals = 0
                                }
                            };
                        }
                        else
                        {
                            baseChainAddress = baseChainAddresses.FirstOrDefault(a => a.Address == w.Address) ?? null;
                            tokenAddress = w;
                        }

                        var baseChainBalance = baseChainAddress?.Balance ?? 0;

                        var tokenBalance = tokenAddress?.Balance ?? 0;
                        var showTokenBalance = tokenBalance != 0;
                        var tokenCode = tokenAddress?.TokenBalance?.Symbol
                            ?? tokenConfig?.DisplayedName
                            ?? string.Empty;
                        var tokenFormat = $"F{Math.Min(tokenAddress?.TokenBalance?.Decimals ?? CurrencyConfig.MaxPrecision, CurrencyConfig.MaxPrecision)}";
                        var intTokenId = (int)(tokenAddress?.TokenBalance?.TokenId ?? 0);
                        var isFreeAddress = w.Address == freeBaseChainAddress.Address && tokenBalance == 0;

                        var baseChainDecimals = account
                            .Currencies
                            .GetByName(baseChainName)
                            .Decimals;

                        var a = new WalletAddressViewModel
                        {
                            WalletAddress    = tokenAddress,
                            Address          = w?.Address,
                            AvailableBalance = baseChainBalance.FromTokens(baseChainDecimals),
                            CurrencyFormat   = currency.Format,
                            CurrencyCode     = baseChainName,
                            IsFreeAddress    = isFreeAddress,
                            ShowTokenBalance = showTokenBalance,
                            HasActivity      = w?.HasActivity ?? false,
                            TokenBalance     = tokenBalance != 0 ? tokenBalance.FromTokens(tokenAddress.TokenBalance.Decimals) : 0,
                            TokenFormat      = tokenFormat,
                            TokenCode        = tokenCode,
                            TokenId          = intTokenId,
                            IsToken          = true
                        };

                        return a;
                    })
                    .ToList();
            }

            var addresses = (await account
                .GetCurrencyAccount(currency.Name)
                .GetAddressesAsync()
                .ConfigureAwait(false))
                .ToList();

            var freeAddress = await account
                .GetFreeExternalAddressAsync(currency.Name)
                .ConfigureAwait(false);

            if (!addresses.Any(w => w.Address == freeAddress.Address))
                addresses.Add(freeAddress);

            return addresses
                .GroupBy(w => w.Address)
                .Select(g =>
                {
                    // main address
                    var address = g.FirstOrDefault(w => w.Currency == currency.Name);
                    var isFreeAddress = address?.Address == freeAddress.Address && freeAddress.Balance == 0;

                    return new WalletAddressViewModel
                    {
                        WalletAddress    = address,
                        Address          = g.Key,
                        HasActivity      = address?.HasActivity ?? false,
                        AvailableBalance = address?.AvailableBalance().FromTokens(currency.Decimals) ?? 0,
                        CurrencyFormat   = currency.Format,
                        CurrencyCode     = currency.DisplayedName,
                        IsFreeAddress    = isFreeAddress,
                        IsToken          = false
                    };
                })
                .ToList();
        }
    }
}