using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;
using Atomex.MarketData.Abstract;
using Atomex.MarketData.Entities;
using Atomex.Wallet.Abstract;
using Atomex.Wallets;
using Atomex.Wallets.Abstract;

namespace Atomex.Swaps.Helpers
{
    public static class RewardForRedeemHelper
    {
        public static async Task<Result<decimal>> EstimateAsync(
            IAccount account,
            IQuotesProvider quotesProvider,
            Func<string, Quote> feeCurrencyQuotesProvider,
            CurrencyConfig redeemableCurrency,
            WalletAddress redeemFromAddress = null,
            CancellationToken cancellationToken = default)
        {
            if (redeemableCurrency is BitcoinBasedConfig)
                return 0m;

            var feeCurrency = redeemableCurrency.FeeCurrencyName;

            var feeCurrencyAddress = redeemFromAddress != null
                ? await account
                    .GetAddressAsync(feeCurrency, redeemFromAddress.Address, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                : null;

            var (redeemFee, redeemFeeError) = await redeemableCurrency
                .GetRedeemFeeAsync(redeemFromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (redeemFeeError != null)
                return redeemFeeError;

            if (feeCurrencyAddress != null && feeCurrencyAddress.AvailableBalance() >= redeemFee)
                return 0m;

            var feeCurrencyToBaseQuote = redeemableCurrency.FeeCurrencyToBaseSymbol != null
                ? quotesProvider?.GetQuote(redeemableCurrency.FeeCurrencyToBaseSymbol)
                : null;

            var feeCurrencyToBasePrice = feeCurrencyToBaseQuote != null
                ? feeCurrencyToBaseQuote.GetMiddlePrice()
                : 0m;

            var feeCurrencyQuote = redeemableCurrency.FeeCurrencySymbol != null
                ? feeCurrencyQuotesProvider.Invoke(redeemableCurrency.FeeCurrencySymbol)
                : null;

            var feeCurrencyPrice = feeCurrencyQuote != null
                ? feeCurrencyQuote.GetMiddlePrice()
                : 0m;

            return await redeemableCurrency
                .GetRewardForRedeemAsync(
                    maxRewardPercent: redeemableCurrency.MaxRewardPercent,
                    maxRewardPercentInBase: redeemableCurrency.MaxRewardPercentInBase,
                    feeCurrencyToBaseSymbol: redeemableCurrency.FeeCurrencyToBaseSymbol,
                    feeCurrencyToBasePrice: feeCurrencyToBasePrice,
                    feeCurrencySymbol: redeemableCurrency.FeeCurrencySymbol,
                    feeCurrencyPrice: feeCurrencyPrice,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
