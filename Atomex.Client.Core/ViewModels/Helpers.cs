using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Subsystems.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex.ViewModels
{
    public static class Helpers
    {
        public class SwapPaymentParams
        {
            public decimal Amount { get; set; }
            public decimal PaymentFee { get; set; }
            public decimal MakerMinerFee { get; set; }
            public decimal ReservedForSwaps { get; set; }
            public Error Error { get; set; }
        }

        public class SwapPriceEstimation
        {
            public decimal TargetAmount { get; set; }
            public decimal OrderPrice { get; set; }
            public decimal Price { get; set; }
            public decimal MaxAmount { get; set; }
            public bool IsNoLiquidity { get; set; }
        }

        public static Task<SwapPaymentParams> EstimateSwapPaymentParamsAsync(
            decimal amount,
            Currency fromCurrency,
            Currency toCurrency,
            IAccount account,
            IAtomexClient atomexClient,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                if (amount == 0)
                {
                    return new SwapPaymentParams
                    {
                        Amount = 0,
                        PaymentFee = 0,
                        MakerMinerFee = 0,
                        ReservedForSwaps = 0,
                        Error = null
                    };
                }

                // estimate max payment amount and max fee
                var (maxAmount, maxFee, _) = await account
                    .EstimateMaxAmountToSendAsync(
                        currency: fromCurrency.Name,
                        to: null,
                        type: BlockchainTransactionType.SwapPayment,
                        fee: 0,
                        feePrice: 0,
                        reserve: true)
                    .ConfigureAwait(false);

                // get amount reserved for active swaps
                var reservedForSwapsAmount = await GetAmountReservedForSwapsAsync(
                        account: account,
                        currency: fromCurrency)
                    .ConfigureAwait(false);

                // estimate maker miner fee
                var estimatedMakerMinerFee = await EstimateMakerMinerFeeAsync(
                        fromCurrency: fromCurrency,
                        toCurrency: toCurrency,
                        account: account,
                        atomexClient: atomexClient,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var hasSameChainForFees = fromCurrency.FeeCurrencyName == fromCurrency.Name;

                var maxNetAmount = Math.Max(maxAmount - reservedForSwapsAmount - estimatedMakerMinerFee, 0m);

                if (maxNetAmount == 0m)
                {
                    return new SwapPaymentParams
                    {
                        Amount = 0m,
                        PaymentFee = 0m,
                        MakerMinerFee = 0m,
                        ReservedForSwaps = 0m,
                        Error = hasSameChainForFees
                            ? new Error(Errors.InsufficientFunds, "Insufficient funds to cover fees")
                            : new Error(Errors.InsufficientChainFunds, string.Format(CultureInfo.InvariantCulture, "Insufficient {0} to cover token transfer fee", fromCurrency.FeeCurrencyName))
                    };
                }

                if (amount > maxNetAmount) // amount greater than max net amount
                {
                    return new SwapPaymentParams
                    {
                        Amount = Math.Max(maxNetAmount, 0m),
                        PaymentFee = maxFee,
                        MakerMinerFee = estimatedMakerMinerFee,
                        ReservedForSwaps = reservedForSwapsAmount,
                        Error = null
                    };
                }

                var estimatedPaymentFee = await account
                    .EstimateFeeAsync(
                        currency: fromCurrency.Name,
                        to: null,
                        amount: amount,
                        type: BlockchainTransactionType.SwapPayment,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (estimatedPaymentFee == null) // wtf? max amount is not null, but estimated fee is null Oo
                {
                    return new SwapPaymentParams
                    {
                        Amount = 0m,
                        PaymentFee = 0m,
                        MakerMinerFee = 0m,
                        ReservedForSwaps = 0m,
                        Error = hasSameChainForFees
                            ? new Error(Errors.InsufficientFunds, "Insufficient funds to cover fees")
                            : new Error(Errors.InsufficientChainFunds, string.Format(CultureInfo.InvariantCulture, "Insufficient {0} to cover token transfer fee", fromCurrency.FeeCurrencyName))
                    };
                }

                return new SwapPaymentParams
                {
                    Amount = amount,
                    PaymentFee = estimatedPaymentFee.Value,
                    MakerMinerFee = estimatedMakerMinerFee,
                    ReservedForSwaps = reservedForSwapsAmount,
                    Error = null
                };

            }, cancellationToken);
        }

        public static Task<SwapPriceEstimation> EstimateSwapPriceAsync(
            decimal amount,
            Currency fromCurrency,
            Currency toCurrency,
            IAccount account,
            IAtomexClient atomexClient,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                if (toCurrency == null)
                    return null;

                var symbol = account.Symbols.SymbolByCurrencies(fromCurrency, toCurrency);
                if (symbol == null)
                    return null;

                var side = symbol.OrderSideForBuyCurrency(toCurrency);
                var orderBook = atomexClient.GetOrderBook(symbol);

                if (orderBook == null)
                    return null;

                var walletAddress = await account
                    .GetRedeemAddressAsync(toCurrency.FeeCurrencyName);

                var baseCurrency = account.Currencies.GetByName(symbol.Base);

                var (estimatedOrderPrice, estimatedPrice) = orderBook.EstimateOrderPrices(
                    side,
                    amount,
                    fromCurrency.DigitsMultiplier,
                    baseCurrency.DigitsMultiplier);

                var estimatedMaxAmount = orderBook.EstimateMaxAmount(side, fromCurrency.DigitsMultiplier);

                var isNoLiquidity = amount != 0 && estimatedOrderPrice == 0;

                var targetAmount = symbol.IsBaseCurrency(toCurrency.Name)
                    ? estimatedPrice != 0
                        ? AmountHelper.RoundDown(amount / estimatedPrice, toCurrency.DigitsMultiplier)
                        : 0m
                    : AmountHelper.RoundDown(amount * estimatedPrice, toCurrency.DigitsMultiplier);

                return new SwapPriceEstimation
                {
                    TargetAmount = targetAmount,
                    OrderPrice = estimatedOrderPrice,
                    Price = estimatedPrice,
                    MaxAmount = estimatedMaxAmount,
                    IsNoLiquidity = isNoLiquidity
                };

            }, cancellationToken);
        }

        public static async Task<decimal> GetAmountReservedForSwapsAsync(
            IAccount account,
            Currency currency)
        {
            var swaps = await account
                .GetSwapsAsync()
                .ConfigureAwait(false);

            var reservedAmount = swaps.Sum(s => (s.IsActive && s.SoldCurrency == currency.Name && !s.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
                ? (s.Symbol.IsBaseCurrency(currency.Name) ? s.Qty : s.Qty * s.Price) + s.MakerMinerFee
                : 0);

            return AmountHelper.RoundDown(reservedAmount, currency.DigitsMultiplier);
        }

        public static async Task<decimal> EstimateMakerMinerFeeAsync(
            Currency fromCurrency,
            Currency toCurrency,
            IAccount account,
            IAtomexClient atomexClient,
            CancellationToken cancellationToken = default)
        {
            var makerPaymentFee = await toCurrency
                .GetPaymentFeeAsync(cancellationToken)
                .ConfigureAwait(false);

            // if toCurrency.Name is not equal toCurrency.FeeCurrencyName convert makerPaymentFee from toCurrency.FeeCurrencyName to toCurrency.Name
            if (toCurrency.Name != toCurrency.FeeCurrencyName)
                makerPaymentFee = ConvertAmount(
                    amount: makerPaymentFee,
                    from: toCurrency.FeeCurrencyName,
                    to: toCurrency.Name,
                    account: account,
                    atomexClient: atomexClient) ?? 0;

            var makerRedeemFee = await fromCurrency
                .GetRedeemFeeAsync(toAddress: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // if fromCurrency.Name is not equal fromCurrency.FeeCurrencyName convert makerRedeemFee from fromCurrency.FeeCurrencyName to fromCurrency.Name
            if (fromCurrency.Name != fromCurrency.FeeCurrencyName)
                makerRedeemFee = ConvertAmount(
                    amount: makerRedeemFee,
                    from: fromCurrency.FeeCurrencyName,
                    to: fromCurrency.Name,
                    account: account,
                    atomexClient: atomexClient) ?? 0;

            // convert makerPaymentFee from toCurrency to fromCurrency
            makerPaymentFee = ConvertAmount(
                amount: makerPaymentFee,
                from: toCurrency.Name,
                to: fromCurrency.Name,
                account: account,
                atomexClient: atomexClient) ?? 0;

            return makerPaymentFee + makerRedeemFee;
        }

        public static decimal? ConvertAmount(
            decimal amount,
            string from,
            string to,
            IAccount account,
            IAtomexClient atomexClient)
        {
            var symbol = account.Symbols.SymbolByCurrencies(from, to);

            var toCurrency = account.Currencies.GetByName(to);

            if (symbol == null)
                throw new Exception($"Can't find symbol for {from} and {to}");

            var quote = atomexClient
                .GetOrderBook(symbol)
                ?.TopOfBook();

            if (quote == null || !quote.IsValid())
                return null;

            var middlePrice = (quote.Ask + quote.Bid) / 2;

            return symbol.IsBaseCurrency(from)
                ? AmountHelper.RoundDown(amount * middlePrice, toCurrency.DigitsMultiplier)
                : AmountHelper.RoundDown(amount / middlePrice, toCurrency.DigitsMultiplier);
        }
    }
}