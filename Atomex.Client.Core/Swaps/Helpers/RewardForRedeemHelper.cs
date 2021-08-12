using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Core;
using Atomex.MarketData;
using Atomex.MarketData.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex.Swaps.Helpers
{
    public static class RewardForRedeemHelper
    {
        public static async Task<decimal> EstimateAsync(
            IAccount account,
            ICurrencyQuotesProvider quotesProvider,
            Func<string, Quote> feeCurrencyQuotesProvider,
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default)
        {
            var currency = account
                .Currencies
                .GetByName(walletAddress.Currency);

            if (currency is BitcoinBasedConfig)
                return 0m;

            var feeCurrency = currency.FeeCurrencyName;

            var feeCurrencyAddress = await account
                .GetAddressAsync(feeCurrency, walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            if (feeCurrencyAddress == null)
            {
                feeCurrencyAddress = await account
                    .GetCurrencyAccount<ILegacyCurrencyAccount>(feeCurrency)
                    .DivideAddressAsync(
                        keyIndex: walletAddress.KeyIndex,
                        keyType: walletAddress.KeyType)
                    .ConfigureAwait(false);

                if (feeCurrencyAddress == null)
                    throw new Exception($"Can't get/devide {currency.Name} address {walletAddress.Address} for {feeCurrency}");
            }

            var redeemFee = await currency
                .GetRedeemFeeAsync(walletAddress, cancellationToken)
                .ConfigureAwait(false);

            if (feeCurrencyAddress.AvailableBalance() >= redeemFee)
                return 0m;

            var feeCurrencyToBaseQuote = currency.FeeCurrencyToBaseSymbol != null
                ? quotesProvider?.GetQuote(currency.FeeCurrencyToBaseSymbol)
                : null;

            var feeCurrencyToBasePrice = feeCurrencyToBaseQuote != null
                ? feeCurrencyToBaseQuote.GetMiddlePrice()
                : 0m;

            var feeCurrencyQuote = currency.FeeCurrencySymbol != null
                ? feeCurrencyQuotesProvider.Invoke(currency.FeeCurrencySymbol)
                : null;

            var feeCurrencyPrice = feeCurrencyQuote != null
                ? feeCurrencyQuote.GetMiddlePrice()
                : 0m;

            return await currency
                .GetRewardForRedeemAsync(
                    maxRewardPercent: currency.MaxRewardPercent,
                    maxRewardPercentInBase: currency.MaxRewardPercentInBase,
                    feeCurrencyToBaseSymbol: currency.FeeCurrencyToBaseSymbol,
                    feeCurrencyToBasePrice: feeCurrencyToBasePrice,
                    feeCurrencySymbol: currency.FeeCurrencySymbol,
                    feeCurrencyPrice: feeCurrencyPrice,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
