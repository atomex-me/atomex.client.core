using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Common;
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
                    fa12Config = null;
                }

                if (tokenContract == null)
                    return Enumerable.Empty<WalletAddressViewModel>();

                var tezosAccount = account
                    .GetCurrencyAccount<TezosAccount>(TezosConfig.Xtz);

                var tezosAddresses = tezosAccount
                    .GetUnspentAddressesAsync()
                    .WaitForResult()
                    .ToDictionary(w => w.Address, w => w);

                var tokenAddresses = tezosAccount.DataRepository
                    .GetTezosTokenAddressesByContractAsync(tokenContract)
                    .WaitForResult();

                return tokenAddresses
                    .Where(w => w.Balance != 0)
                    .Select(w =>
                    {
                        var tokenBalance = w.Balance;

                        var showTokenBalance = tokenBalance != 0;

                        var tokenCode = w.TokenBalance?.Symbol ?? fa12Config?.Name ?? "TOKENS";

                        var tezosBalance = tezosAddresses.TryGetValue(w.Address, out var tezosAddress)
                            ? tezosAddress.AvailableBalance()
                            : 0m;

                        return new WalletAddressViewModel
                        {
                            WalletAddress = w,
                            Address = w.Address,
                            AvailableBalance = tezosBalance,
                            CurrencyFormat = currency.Format,
                            CurrencyCode = currency.Name,
                            IsFreeAddress = false,
                            ShowTokenBalance = showTokenBalance,
                            TokenBalance = tokenBalance,
                            TokenFormat = $"F{Math.Min(w.TokenBalance?.Decimals ?? MaxTokenCurrencyFormatDecimals, MaxTokenCurrencyFormatDecimals)}",
                            TokenCode = tokenCode,
                            TokenId = w.TokenBalance?.TokenId ?? 0,
                            IsTezosToken = true
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
                        WalletAddress = address,
                        Address = g.Key,
                        HasActivity = address?.HasActivity ?? false,
                        AvailableBalance = address?.AvailableBalance() ?? 0m,
                        CurrencyFormat = currency.Format,
                        CurrencyCode = currency.Name,
                        IsFreeAddress = isFreeAddress,
                        IsTezosToken = false
                    };
                });
        }
    }
}