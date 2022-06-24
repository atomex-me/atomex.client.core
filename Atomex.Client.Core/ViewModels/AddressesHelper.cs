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
            string tokenContract = null,
            int tokenId = 0)
        {
            var isTezosToken = Currencies.IsTezosToken(currency.Name) || tokenContract != null;

            if (isTezosToken)
            {
                if (currency is TezosTokenConfig tezosTokenConfig)
                {
                    tokenContract ??= tezosTokenConfig.TokenContractAddress;
                }
                else
                {
                    tezosTokenConfig = account.Currencies
                        .FirstOrDefault(c => c is TezosTokenConfig t &&
                                             t.TokenContractAddress == tokenContract &&
                                             t.TokenId == tokenId) as TezosTokenConfig;
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

                var tokenAddresses = (await tezosAccount.DataRepository
                    .GetTezosTokenAddressesByContractAsync(tokenContract)
                    .ConfigureAwait(false))
                    .Where(w => w.Currency == "FA12" || w.Currency == "FA2");

                tokenAddresses = tokenAddresses.Where(wa => wa.TokenBalance?.TokenId == tokenId);

                var tezosAddressesWithoutTokens = tezosAddresses
                    .Where(w => tokenAddresses.All(ta => ta.Address != w.Address));

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
                                Address     = w.Address,
                                Currency    = tezosTokenConfig is Fa12Config ? "FA12" : "FA2",
                                Balance     = 0,
                                HasActivity = false,
                                KeyIndex    = w.KeyIndex,
                                KeyType     = w.KeyType,
                                TokenBalance = new Blockchain.Tezos.TokenBalance
                                {
                                    Contract = tokenContract,
                                    Balance  = "0",
                                    Symbol   = tezosTokenConfig?.DisplayedName ?? "TOKENS",
                                    Decimals = 0
                                }
                            };
                        }
                        else
                        {
                            tezosAddress = tezosAddresses.FirstOrDefault(a => a.Address == w.Address) ?? null;
                            tokenAddress = w;
                        }

                        var tezosBalance = tezosAddress?.AvailableBalance() ?? 0;

                        var tokenBalance = tokenAddress?.Balance ?? 0;
                        var showTokenBalance = tokenBalance != 0;
                        var tokenCode = tokenAddress?.TokenBalance?.Symbol ?? tezosTokenConfig?.DisplayedName ?? "TOKENS";
                        var tokenFormat =
                            $"F{Math.Min(tokenAddress?.TokenBalance?.Decimals ?? MaxTokenCurrencyFormatDecimals, MaxTokenCurrencyFormatDecimals)}";
                        var tokenId = tokenAddress?.TokenBalance?.TokenId ?? 0;

                        var isFreeAddress = w.Address == freeTezosAddress.Address && tokenBalance == 0;

                        return new WalletAddressViewModel
                        {
                            WalletAddress    = tokenAddress,
                            Address          = w?.Address,
                            AvailableBalance = tezosBalance,
                            CurrencyFormat   = currency.Format,
                            CurrencyCode     = currency.DisplayedName,
                            IsFreeAddress    = isFreeAddress,
                            ShowTokenBalance = showTokenBalance,
                            HasActivity      = w?.HasActivity ?? false,
                            TokenBalance     = tokenBalance,
                            TokenFormat      = tokenFormat,
                            TokenCode        = tokenCode,
                            TokenId          = (int)tokenId,
                            IsTezosToken     = true
                        };
                    });
            }

            var addresses = (await account.GetCurrencyAccount(currency.Name)
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
                        AvailableBalance = address?.AvailableBalance() ?? 0m,
                        CurrencyFormat   = currency.Format,
                        CurrencyCode     = currency.DisplayedName,
                        IsFreeAddress    = isFreeAddress,
                        IsTezosToken     = false
                    };
                });
        }

        public static string TruncateAddress(this string address, int leftLength = 4, int rightLength = 5)
        {
            if (string.IsNullOrEmpty(address) || address.Length < 9)
                return address;

            return $"{address[..leftLength]}···{address.Substring(address.Length - rightLength, rightLength)}";
        }
    }
}