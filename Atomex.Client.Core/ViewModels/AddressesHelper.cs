using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Atomex.Core;
using Atomex.TezosTokens;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.Tezos;

namespace Atomex.ViewModels
{
    public static class AddressesHelper
    {
        public const int MaxTokenCurrencyFormatDecimals = 9;

        public static async Task<IEnumerable<WalletAddressViewModel>> GetReceivingAddressesAsync(
            IAccount account,
            CurrencyConfig currency,
            string tokenContract = null)
        {
            var isTezosToken = Currencies.IsTezosToken(currency.Name) || tokenContract != null;

            if (isTezosToken)
            {
                if (currency is Fa12Config fa12Config)
                {
                    tokenContract ??= fa12Config.TokenContractAddress;
                }
                else
                {
                    fa12Config = account.Currencies
                        .FirstOrDefault(c => c is Fa12Config fa12 && fa12.TokenContractAddress == tokenContract) as Fa12Config;
                }

                if (tokenContract == null)
                    return Enumerable.Empty<WalletAddressViewModel>();

                var tezosAccount = account
                    .GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz);

                var unspentTezosAddresses = await tezosAccount
                    .GetUnspentAddressesAsync()
                    .ConfigureAwait(false);

                var freeTezosAddress = await tezosAccount
                    .GetFreeExternalAddressAsync()
                    .ConfigureAwait(false);

                var tezosAddresses = unspentTezosAddresses
                    .Concat(new[] { freeTezosAddress });

                var tezosAddressesDictionary = tezosAddresses
                    .ToDictionary(w => w.Address, w => w);

                var tokenAddresses = (await tezosAccount.DataRepository
                    .GetTezosTokenAddressesByContractAsync(tokenContract)
                    .ConfigureAwait(false))
                    .Where(w => w.Currency == "FA12" || w.Currency == "FA2");

                var tezosAddressesWithoutTokens = tezosAddresses
                    .Where(w => !tokenAddresses.Any(ta => ta.Address == w.Address));

                return tokenAddresses
                    .Concat(tezosAddressesWithoutTokens)
                    .Select(w =>
                    {
                        WalletAddress tezosAddress, tokenAddress;

                        if (w.Currency == "XTZ")
                        {
                            tezosAddress = w;
                            tokenAddress = new WalletAddress
                            {
                                Address      = w.Address,
                                Currency     = fa12Config != null ? "FA12" : "FA2",
                                Balance      = 0,
                                HasActivity  = false,
                                KeyIndex     = w.KeyIndex,
                                KeyType      = w.KeyType,
                                TokenBalance = new Blockchain.Tezos.TokenBalance
                                {
                                    Balance  = "0",
                                    Token = new Blockchain.Tezos.Token()
                                    {
                                        Contract = tokenContract,
                                        Symbol   = fa12Config?.Name ?? "TOKENS",
                                        Decimals = 0,
                                    }
                                }
                            };
                        }
                        else
                        {
                            tezosAddress = null;
                            tokenAddress = w;
                        }

                        var tezosBalance = tezosAddress?.AvailableBalance() ?? 0;
                        var token = tokenAddress?.TokenBalance?.Token;

                        var tokenBalance     = tokenAddress?.Balance ?? 0;
                        var showTokenBalance = tokenBalance != 0;
                        var tokenCode        = token?.Symbol ?? fa12Config?.Name ?? "TOKENS";
                        var tokenFormat      = $"F{Math.Min(token?.Decimals ?? MaxTokenCurrencyFormatDecimals, MaxTokenCurrencyFormatDecimals)}";
                        var tokenId          = token?.TokenId ?? 0;

                        var isFreeAddress = w.Address == freeTezosAddress.Address;

                        return new WalletAddressViewModel
                        {
                            WalletAddress    = tokenAddress,
                            Address          = w.Address,
                            AvailableBalance = tezosBalance,
                            CurrencyFormat   = currency.Format,
                            CurrencyCode     = currency.Name,
                            IsFreeAddress    = isFreeAddress,
                            ShowTokenBalance = showTokenBalance,
                            TokenBalance     = tokenBalance,
                            TokenFormat      = tokenFormat,
                            TokenCode        = tokenCode,
                            TokenId          = tokenId,
                            IsTezosToken     = true
                        };
                    });
            }

            // get all nonzero addresses
            var activeAddresses = (await account
                .GetUnspentAddressesAsync(currency.Name)
                .ConfigureAwait(false))
                .ToList();

            // get free external address
            var freeAddress = await account
                .GetFreeExternalAddressAsync(currency.Name)
                .ConfigureAwait(false);

            return activeAddresses
                .Concat(new[] { freeAddress })
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
                        AvailableBalance = address?.AvailableBalance() ?? 0m,
                        CurrencyFormat   = currency.Format,
                        CurrencyCode     = currency.Name,
                        IsFreeAddress    = isFreeAddress,
                        IsTezosToken     = false
                    };
                });
        }
    }
}