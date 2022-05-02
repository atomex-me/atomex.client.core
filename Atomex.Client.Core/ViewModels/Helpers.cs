using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Atomex.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography.Abstract;
using Atomex.MarketData.Abstract;
using Atomex.Services.Abstract;
using Atomex.Swaps.Helpers;
using Atomex.Wallet.Abstract;

namespace Atomex.ViewModels
{
    public static class Helpers
    {
        public class SwapParams
        {
            public decimal Amount { get; set; }
            public decimal PaymentFee { get; set; }
            public decimal RedeemFee { get; set; }
            public decimal RewardForRedeem { get; set; }
            public decimal MakerNetworkFee { get; set; }
            public decimal ReservedForSwaps { get; set; }
            public Error Error { get; set; }

            public bool HasRewardForRedeem => RewardForRedeem != 0;
        }


        public class UserMessage
        {
            public int Id { get; set; }
            public string UserId { get; set; }
            public string Message { get; set; }
            public bool IsReaded { get; set; }
        }


        public enum SwapDetailingStatus
        {
            Initialization,
            Exchanging,
            Completion
        }

        public class DetailsLink
        {
            public string Text { get; set; }
            public string Url { get; set; }
        }

        public class SwapDetailingInfo
        {
            public bool IsCompleted { get; set; }
            public DetailsLink ExplorerLink { get; set; }
            public SwapDetailingStatus Status { get; set; }
            public string Description { get; set; }
        }

        public class SwapPriceEstimation
        {
            public decimal FromAmount { get; set; }
            public decimal ToAmount { get; set; }
            public decimal OrderPrice { get; set; }
            public decimal Price { get; set; }
            public decimal MaxFromAmount { get; set; }
            public decimal MaxToAmount { get; set; }
            public bool IsNoLiquidity { get; set; }
        }


        public static IEnumerable<SwapDetailingInfo> GetSwapDetailingInfo(Swap swap, ICurrencies currencies)
        {
            var soldCurrencyConfig = currencies.GetByName(swap.SoldCurrency);
            var purchaseCurrencyConfig = currencies.GetByName(swap.PurchasedCurrency);
            IList<SwapDetailingInfo> result = new List<SwapDetailingInfo>();

            if (swap.StateFlags.HasFlag(SwapStateFlags.HasSecretHash) &&
                swap.Status.HasFlag(SwapStatus.Initiated) &&
                swap.Status.HasFlag(SwapStatus.Accepted))
            {
                result.Add(new SwapDetailingInfo
                {
                    Status = SwapDetailingStatus.Initialization,
                    IsCompleted = true,
                    Description = "Orders matched"
                });

                result.Add(new SwapDetailingInfo
                {
                    Status = SwapDetailingStatus.Initialization,
                    IsCompleted = true,
                    Description = "Credentials exchanged"
                });
            }
            else
            {
                if (swap.IsCanceled)
                {
                    result.Add(new SwapDetailingInfo
                    {
                        Status = SwapDetailingStatus.Initialization,
                        IsCompleted = false,
                        Description = "Error during orders matching and credentials exchanging"
                    });

                    return result;
                }

                result.Add(new SwapDetailingInfo
                {
                    Status = SwapDetailingStatus.Initialization,
                    IsCompleted = false,
                    Description = "Waiting while orders are matched and credentials exchanged"
                });

                return result;
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.HasPartyPayment) &&
                swap.StateFlags.HasFlag(SwapStateFlags.IsPartyPaymentConfirmed))
            {
                var swapDetailingStep = new SwapDetailingInfo
                {
                    Status = SwapDetailingStatus.Exchanging,
                    IsCompleted = false,
                    Description = $"{swap.PurchasedCurrency} counterparty payment",
                    ExplorerLink = new DetailsLink
                    {
                        Text = "transaction confirmed",
                        Url = purchaseCurrencyConfig switch
                        {
                            EthereumConfig ethereumConfig =>
                                $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                            TezosConfig tezosConfig =>
                                $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                            _ => null
                        }
                    }
                };

                result.Add(swapDetailingStep);
            }
            else
            {
                if (swap.IsCanceled)
                {
                    var swapDetailingStep = new SwapDetailingInfo
                    {
                        Status = SwapDetailingStatus.Exchanging,
                        IsCompleted = false,
                        Description = $"Counterparty {swap.PurchasedCurrency} payment",
                        ExplorerLink = new DetailsLink
                        {
                            Text = "transaction failed",
                            Url = purchaseCurrencyConfig switch
                            {
                                EthereumConfig ethereumConfig =>
                                    $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                                TezosConfig tezosConfig =>
                                    $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                                _ => null
                            }
                        }
                    };

                    result.Add(swapDetailingStep);
                    return result;
                }
                else
                {
                    var swapDetailingStep = new SwapDetailingInfo
                    {
                        Status = SwapDetailingStatus.Exchanging,
                        IsCompleted = false,
                        Description =
                            $"Waiting for confirmation counterparty {swap.PurchasedCurrency}",
                        ExplorerLink = new DetailsLink
                        {
                            Text = "payment transaction",
                            Url = purchaseCurrencyConfig switch
                            {
                                EthereumConfig ethereumConfig =>
                                    $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                                TezosConfig tezosConfig =>
                                    $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                                _ => null
                            }
                        }
                    };

                    result.Add(swapDetailingStep);
                    return result;
                }
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentConfirmed) || swap.IsComplete || swap.IsRefunded ||
                swap.IsUnsettled || swap.IsCanceled && swap.StateFlags.HasFlag(SwapStateFlags.HasSecret))
            {
                var swapDetailingStep = new SwapDetailingInfo
                {
                    Status = SwapDetailingStatus.Exchanging,
                    IsCompleted = true,
                    Description = $"Your {swap.SoldCurrency} payment",
                    ExplorerLink = new DetailsLink
                    {
                        Text = "transaction confirmed",
                        Url = soldCurrencyConfig switch
                        {
                            EthereumConfig ethereumConfig =>
                                $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                            TezosConfig tezosConfig =>
                                $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                            _ => null
                        }
                    }
                };

                result.Add(swapDetailingStep);
            }
            else
            {
                if (swap.IsCanceled)
                {
                    var swapDetailingStep = new SwapDetailingInfo
                    {
                        Status = SwapDetailingStatus.Exchanging,
                        IsCompleted = false,
                        Description = $"Your {swap.SoldCurrency} payment",

                        ExplorerLink = new DetailsLink
                        {
                            Text = "transaction failed",
                            Url = soldCurrencyConfig switch
                            {
                                EthereumConfig ethereumConfig =>
                                    $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                                TezosConfig tezosConfig =>
                                    $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                                _ => null
                            }
                        }
                    };

                    result.Add(swapDetailingStep);
                    return result;
                }

                // your payment broadcasted but not confirmed.
                if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
                {
                    var swapDetailingStep = new SwapDetailingInfo
                    {
                        Status = SwapDetailingStatus.Exchanging,
                        IsCompleted = false,
                        Description = $"Waiting for confirmation your {swap.SoldCurrency}",
                        ExplorerLink = new DetailsLink
                        {
                            Text = "payment transaction",
                            Url = soldCurrencyConfig switch
                            {
                                EthereumConfig ethereumConfig =>
                                    $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                                TezosConfig tezosConfig =>
                                    $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                                _ => null
                            }
                        }
                    };

                    result.Add(swapDetailingStep);
                    return result;
                }

                // your payment not yet created.
                result.Add(new SwapDetailingInfo
                {
                    Status = SwapDetailingStatus.Exchanging,
                    IsCompleted = false,
                    Description = $"Creating your {swap.SoldCurrency} payment transaction."
                });

                return result;
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.HasSecret))
            {
                var swapDetailingStep = new SwapDetailingInfo
                {
                    Status = SwapDetailingStatus.Completion,
                    IsCompleted = false,
                    Description = $"Counterparty {swap.SoldCurrency} redeem",
                    ExplorerLink = new DetailsLink
                    {
                        Text = "transaction confirmed",
                        Url = soldCurrencyConfig switch
                        {
                            EthereumConfig ethereumConfig =>
                                $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                            TezosConfig tezosConfig =>
                                $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                            _ => null
                        }
                    }
                };

                result.Add(swapDetailingStep);
            }
            else
            {
                if (swap.IsCanceled)
                {
                    var swapDetailingStep = new SwapDetailingInfo
                    {
                        Status = SwapDetailingStatus.Completion,
                        IsCompleted = false,
                        Description = $"Counterparty {swap.SoldCurrency} redeem",
                        ExplorerLink = new DetailsLink
                        {
                            Text = "transaction failed",
                            Url = soldCurrencyConfig switch
                            {
                                EthereumConfig ethereumConfig =>
                                    $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                                TezosConfig tezosConfig =>
                                    $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                                _ => null
                            }
                        }
                    };

                    result.Add(swapDetailingStep);
                    return result;
                }
                else
                {
                    var swapDetailingStep = new SwapDetailingInfo
                    {
                        Status = SwapDetailingStatus.Completion,
                        IsCompleted = false,
                        Description = $"Waiting for confirmation counterparty {swap.SoldCurrency}",
                        ExplorerLink = new DetailsLink
                        {
                            Text = "redeem transaction",
                            Url = soldCurrencyConfig switch
                            {
                                EthereumConfig ethereumConfig =>
                                    $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                                TezosConfig tezosConfig =>
                                    $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                                _ => null
                            }
                        }
                    };

                    result.Add(swapDetailingStep);
                    return result;
                }
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemConfirmed))
            {
                var swapDetailingStep = new SwapDetailingInfo
                {
                    Status = SwapDetailingStatus.Completion,
                    IsCompleted = true,
                    Description = $"Your {swap.PurchasedCurrency} redeem",
                    ExplorerLink = new DetailsLink
                    {
                        Text = "transaction confirmed",
                        Url = purchaseCurrencyConfig switch
                        {
                            EthereumConfig ethereumConfig =>
                                $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                            TezosConfig tezosConfig =>
                                $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                            _ => null
                        }
                    }
                };

                result.Add(swapDetailingStep);
            }

            else
            {
                if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundConfirmed))
                {
                    var swapDetailingStep = new SwapDetailingInfo
                    {
                        Status = SwapDetailingStatus.Completion,
                        IsCompleted = true,
                        Description = $"Your {swap.SoldCurrency} refund",
                        ExplorerLink = new DetailsLink
                        {
                            Text = "transaction confirmed",
                            Url = soldCurrencyConfig switch
                            {
                                EthereumConfig ethereumConfig =>
                                    $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                                TezosConfig tezosConfig =>
                                    $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                                _ => null
                            }
                        }
                    };

                    result.Add(swapDetailingStep);
                }
                else if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast))
                {
                    var swapDetailingStep = new SwapDetailingInfo
                    {
                        Status = SwapDetailingStatus.Completion,
                        IsCompleted = true,
                        Description = $"Waiting for confirmation your {swap.SoldCurrency}",
                        ExplorerLink = new DetailsLink
                        {
                            Text = "refund transaction",
                            Url = soldCurrencyConfig switch
                            {
                                EthereumConfig ethereumConfig =>
                                    $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                                TezosConfig tezosConfig =>
                                    $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                                _ => null
                            }
                        }
                    };

                    result.Add(swapDetailingStep);
                }
                else
                {
                    if (swap.IsCanceled)
                    {
                        var swapDetailingStep = new SwapDetailingInfo
                        {
                            Status = SwapDetailingStatus.Completion,
                            IsCompleted = false,
                            Description = $"Your {swap.PurchasedCurrency} redeem",
                            ExplorerLink = new DetailsLink
                            {
                                Text = "transaction failed",
                                Url = purchaseCurrencyConfig switch
                                {
                                    EthereumConfig ethereumConfig =>
                                        $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                                    TezosConfig tezosConfig =>
                                        $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                                    _ => null
                                }
                            }
                        };

                        result.Add(swapDetailingStep);
                    }
                    else
                    {
                        var swapDetailingStep = new SwapDetailingInfo
                        {
                            Status = SwapDetailingStatus.Completion,
                            IsCompleted = false,
                            Description = $"Waiting for confirmation your {swap.PurchasedCurrency}",
                            ExplorerLink = new DetailsLink
                            {
                                Text = "redeem transaction",
                                Url = purchaseCurrencyConfig switch
                                {
                                    EthereumConfig ethereumConfig =>
                                        $"{ethereumConfig.AddressExplorerUri}{ethereumConfig.SwapContractAddress}",

                                    TezosConfig tezosConfig =>
                                        $"{tezosConfig.AddressExplorerUri}{tezosConfig.SwapContractAddress}",
                                    _ => null
                                }
                            }
                        };

                        result.Add(swapDetailingStep);
                    }
                }
            }

            return result;
        }

        public static Task<SwapParams> EstimateSwapParamsAsync(
            IFromSource from,
            decimal fromAmount,
            string redeemFromAddress,
            CurrencyConfig fromCurrency,
            CurrencyConfig toCurrency,
            IAccount_OLD account,
            IAtomexClient_OLD atomexClient,
            ISymbolsProvider symbolsProvider,
            ICurrencyQuotesProvider quotesProvider,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                if (fromCurrency == null)
                    return null;

                if (toCurrency == null)
                    return null;

                // get redeem address for ToCurrency base blockchain
                var redeemFromWalletAddress = redeemFromAddress != null
                    ? await account
                        .GetAddressAsync(toCurrency.FeeCurrencyName, redeemFromAddress, cancellationToken)
                        .ConfigureAwait(false)
                    : null;

                // estimate redeem fee
                var estimatedRedeemFee = await toCurrency
                    .GetEstimatedRedeemFeeAsync(redeemFromWalletAddress, withRewardForRedeem: false)
                    .ConfigureAwait(false);

                // estimate reward for redeem
                var rewardForRedeem = await RewardForRedeemHelper.EstimateAsync(
                    account: account,
                    quotesProvider: quotesProvider,
                    feeCurrencyQuotesProvider: symbol => atomexClient?.GetOrderBook(symbol)?.TopOfBook(),
                    redeemableCurrency: toCurrency,
                    redeemFromAddress: redeemFromWalletAddress,
                    cancellationToken: cancellationToken);

                // get amount reserved for active swaps
                var reservedForSwapsAmount = await GetAmountReservedForSwapsAsync(
                        from: from,
                        account: account,
                        currency: fromCurrency)
                    .ConfigureAwait(false);

                // estimate maker network fee
                var estimatedMakerNetworkFee = await EstimateMakerNetworkFeeAsync(
                        fromCurrency: fromCurrency,
                        toCurrency: toCurrency,
                        account: account,
                        atomexClient: atomexClient,
                        symbolsProvider: symbolsProvider,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var fromCurrencyAccount = account
                    .GetCurrencyAccount(fromCurrency.Name) as IEstimatable;

                // estimate payment fee
                var estimatedPaymentFee = await fromCurrencyAccount
                    .EstimateSwapPaymentFeeAsync(
                        from: from,
                        amount: fromAmount,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                // estimate max amount and max fee
                var maxAmountEstimation = await fromCurrencyAccount
                    .EstimateMaxSwapPaymentAmountAsync(
                        from: from,
                        reserve: true,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
 
                if (maxAmountEstimation.Error != null)
                {
                    return new SwapParams
                    {
                        Amount           = 0m,
                        PaymentFee       = estimatedPaymentFee.Value,
                        RedeemFee        = estimatedRedeemFee,
                        RewardForRedeem  = rewardForRedeem,
                        MakerNetworkFee  = estimatedMakerNetworkFee,
                        ReservedForSwaps = reservedForSwapsAmount,
                        Error            = maxAmountEstimation.Error
                    };
                }

                var maxNetAmount = Math.Max(maxAmountEstimation.Amount - reservedForSwapsAmount - estimatedMakerNetworkFee, 0m);

                if (maxNetAmount == 0m) // insufficient funds
                {
                    return new SwapParams
                    {
                        Amount           = 0m,
                        PaymentFee       = maxAmountEstimation.Fee,
                        RedeemFee        = estimatedRedeemFee,
                        RewardForRedeem  = rewardForRedeem,
                        MakerNetworkFee  = estimatedMakerNetworkFee,
                        ReservedForSwaps = reservedForSwapsAmount,
                        Error = new Error(
                            code: Errors.InsufficientFunds,
                            description: Resources.InsufficientFundsToCoverMakerNetworkFee,
                            details: string.Format(Resources.InsufficientFundsToCoverMakerNetworkFeeDetails,
                                estimatedMakerNetworkFee,                             // required
                                fromCurrency.Name,                                    // currency code
                                maxAmountEstimation.Amount - reservedForSwapsAmount)) // available
                    };
                }

                if (fromAmount > maxNetAmount) // amount greater than max net amount => use max amount params
                {
                    return new SwapParams
                    {
                        Amount           = Math.Max(maxNetAmount, 0m),
                        PaymentFee       = maxAmountEstimation.Fee,
                        RedeemFee        = estimatedRedeemFee,
                        RewardForRedeem  = rewardForRedeem,
                        MakerNetworkFee  = estimatedMakerNetworkFee,
                        ReservedForSwaps = reservedForSwapsAmount,
                        Error = new Error(
                            code: Errors.InsufficientFunds,
                            description: Resources.InsufficientFunds,
                            details: string.Format(Resources.InsufficientFundsToSendAmountDetails,
                                fromAmount,        // required
                                fromCurrency.Name, // currency code
                                maxNetAmount))     // available
                    };
                }

                return new SwapParams
                {
                    Amount           = fromAmount,
                    PaymentFee       = estimatedPaymentFee.Value,
                    RedeemFee        = estimatedRedeemFee,
                    RewardForRedeem  = rewardForRedeem,
                    MakerNetworkFee  = estimatedMakerNetworkFee,
                    ReservedForSwaps = reservedForSwapsAmount,
                    Error            = null
                };

            }, cancellationToken);
        }

        public static Task<SwapPriceEstimation> EstimateSwapPriceAsync(
            decimal amount,
            AmountType amountType, 
            CurrencyConfig fromCurrency,
            CurrencyConfig toCurrency,
            IAccount_OLD account,
            IAtomexClient_OLD atomexClient,
            ISymbolsProvider symbolsProvider,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                if (fromCurrency == null)
                    return null;

                if (toCurrency == null)
                    return null;

                var symbol = symbolsProvider
                    .GetSymbols(account.Network)
                    .SymbolByCurrencies(fromCurrency, toCurrency);

                if (symbol == null)
                    return null;

                var side = symbol.OrderSideForBuyCurrency(toCurrency);
                var orderBook = atomexClient.GetOrderBook(symbol);

                if (orderBook == null)
                    return null;

                var baseCurrency = account.Currencies.GetByName(symbol.Base);

                var isSoldAmount = amountType == AmountType.Sold;

                var (estimatedOrderPrice, estimatedPrice) = orderBook.EstimateOrderPrices(
                    side: side,
                    amount: amount,
                    amountDigitsMultiplier: isSoldAmount
                        ? fromCurrency.DigitsMultiplier
                        : toCurrency.DigitsMultiplier,
                    qtyDigitsMultiplier: baseCurrency.DigitsMultiplier,
                    amountType: amountType);

                var (estimatedMaxFromAmount, estimatedMaxToAmount) = orderBook.EstimateMaxAmount(side, fromCurrency.DigitsMultiplier);

                var isNoLiquidity = amount != 0 && estimatedOrderPrice == 0;

                var oppositeAmount = isSoldAmount
                    ? symbol.IsBaseCurrency(toCurrency.Name)
                        ? estimatedPrice != 0
                            ? AmountHelper.RoundDown(amount / estimatedPrice, toCurrency.DigitsMultiplier)
                            : 0m
                        : AmountHelper.RoundDown(amount * estimatedPrice, toCurrency.DigitsMultiplier)
                    : symbol.IsBaseCurrency(toCurrency.Name)
                        ? AmountHelper.RoundDown(amount * estimatedPrice, fromCurrency.DigitsMultiplier)
                        : estimatedPrice != 0
                            ? AmountHelper.RoundDown(amount / estimatedPrice, fromCurrency.DigitsMultiplier)
                            : 0m;

                return new SwapPriceEstimation
                {
                    FromAmount    = isSoldAmount ? amount : oppositeAmount, 
                    ToAmount      = isSoldAmount ? oppositeAmount : amount,
                    OrderPrice    = estimatedOrderPrice,
                    Price         = estimatedPrice,
                    MaxFromAmount = estimatedMaxFromAmount,
                    MaxToAmount   = estimatedMaxToAmount,
                    IsNoLiquidity = isNoLiquidity
                };

            }, cancellationToken);
        }

        public static async Task<decimal> GetAmountReservedForSwapsAsync(
            IFromSource from,
            IAccount_OLD account,
            CurrencyConfig currency)
        {
            var swaps = await account
                .GetSwapsAsync()
                .ConfigureAwait(false);

            var reservedAmount = 0m;

            foreach (var swap in swaps)
            {
                if (!swap.IsActive ||
                    swap.SoldCurrency != currency.Name ||
                    swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
                    continue;

                if (from is FromAddress fromAddress && fromAddress.Address == swap.FromAddress)
                {
                    reservedAmount += (swap.Symbol.IsBaseCurrency(currency.Name) ? swap.Qty : swap.Qty * swap.Price) + swap.MakerNetworkFee;
                }
                else if (from is FromOutputs fromOutputs)
                {
                    if (currency is not BitcoinBasedConfig bitcoinBasedConfig)
                        continue;

                    foreach (var fromOutput in fromOutputs.Outputs)
                    {
                        var isUsed = swap.FromOutputs.Any(o => o.TxId == fromOutput.TxId && o.Index == fromOutput.Index);

                        if (isUsed)
                            reservedAmount += bitcoinBasedConfig.SatoshiToCoin(fromOutput.Value);
                    }
                }
            }

            return AmountHelper.RoundDown(reservedAmount, currency.DigitsMultiplier);
        }

        public static async Task<decimal> EstimateMakerNetworkFeeAsync(
            CurrencyConfig fromCurrency,
            CurrencyConfig toCurrency,
            IAccount_OLD account,
            IAtomexClient_OLD atomexClient,
            ISymbolsProvider symbolsProvider,
            CancellationToken cancellationToken = default)
        {
            var makerPaymentFee = await toCurrency
                .GetPaymentFeeAsync(cancellationToken)
                .ConfigureAwait(false);

            // if toCurrency.Name is not equal toCurrency.FeeCurrencyName convert makerPaymentFee from toCurrency.FeeCurrencyName to toCurrency.Name
            if (toCurrency.Name != toCurrency.FeeCurrencyName)
                makerPaymentFee = TryConvertAmount(
                    amount: makerPaymentFee,
                    from: toCurrency.FeeCurrencyName,
                    to: toCurrency.Name,
                    account: account,
                    atomexClient: atomexClient,
                    symbolsProvider: symbolsProvider) ?? 0;

            var makerRedeemFee = await fromCurrency
                .GetEstimatedRedeemFeeAsync(
                    toAddress: null,
                    withRewardForRedeem: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // if fromCurrency.Name is not equal fromCurrency.FeeCurrencyName convert makerRedeemFee from fromCurrency.FeeCurrencyName to fromCurrency.Name
            if (fromCurrency.Name != fromCurrency.FeeCurrencyName)
                makerRedeemFee = TryConvertAmount(
                    amount: makerRedeemFee,
                    from: fromCurrency.FeeCurrencyName,
                    to: fromCurrency.Name,
                    account: account,
                    atomexClient: atomexClient,
                    symbolsProvider: symbolsProvider) ?? 0;

            // convert makerPaymentFee from toCurrency to fromCurrency
            makerPaymentFee = TryConvertAmount(
                amount: makerPaymentFee,
                from: toCurrency.Name,
                to: fromCurrency.Name,
                account: account,
                atomexClient: atomexClient,
                symbolsProvider: symbolsProvider) ?? 0;

            return makerPaymentFee + makerRedeemFee;
        }

        public static decimal? ConvertAmount(
            decimal amount,
            string from,
            string to,
            IAccount_OLD account,
            IAtomexClient_OLD atomexClient,
            ISymbolsProvider symbolsProvider)
        {
            var symbol = symbolsProvider
                .GetSymbols(account.Network)
                .SymbolByCurrencies(from, to);

            var toCurrency = account.Currencies.GetByName(to);

            if (symbol == null || toCurrency == null)
                return null;

            var quote = atomexClient
                .GetOrderBook(symbol)
                ?.TopOfBook();

            if (quote == null)
                return null;

            var middlePrice = quote.GetMiddlePrice();

            if (middlePrice == 0)
                return null;

            return symbol.IsBaseCurrency(from)
                ? AmountHelper.RoundDown(amount * middlePrice, toCurrency.DigitsMultiplier)
                : AmountHelper.RoundDown(amount / middlePrice, toCurrency.DigitsMultiplier);
        }

        public static decimal? TryConvertAmount(
            decimal amount,
            string from,
            string to,
            IAccount_OLD account,
            IAtomexClient_OLD atomexClient,
            ISymbolsProvider symbolsProvider)
        {
            // firstly try direct conversion first
            var result = ConvertAmount(amount, from, to, account, atomexClient, symbolsProvider);

            if (result != null)
                return result;

            // try to use intermediate currency (BTC and ETH)
            foreach (var currency in new string[]{ "BTC", "ETH "})
            {
                var amountInCurrency = ConvertAmount(amount, from, currency, account, atomexClient, symbolsProvider);

                if (amountInCurrency == null)
                    continue;

                result = ConvertAmount(amountInCurrency.Value, currency, to, account, atomexClient, symbolsProvider);

                if (result != null)
                    return result;
            }

            return null;
        }

        public static string GetUserId(IAccount_OLD account)
        {
            using var servicePublicKey =
                account.Wallet.GetServicePublicKey(account.UserSettings.AuthenticationKeyIndex);
            var publicKey = servicePublicKey.ToUnsecuredBytes();

            return HashAlgorithm.Sha256.Hash(publicKey, iterations: 2).ToHexString();
        }

        public static async Task<List<UserMessage>> GetUserMessages(string userId,
            CancellationToken cancellationToken = default)
        {
            return await HttpHelper.GetAsync(
                    baseUri: "https://test.atomex.me/",
                    requestUri: $"usermessages/get_user_messages/?uid={userId}&format=json",
                    responseHandler: response =>
                        JsonConvert.DeserializeObject<List<UserMessage>>(response.Content.ReadAsStringAsync()
                            .WaitForResult()),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public static async Task<HttpResponseMessage> MarkUserMessageReaded(int messageId,
            CancellationToken cancellationToken = default)
        {
            return await HttpHelper.PostAsync(
                    baseUri: "https://test.atomex.me/",
                    content: null,
                    requestUri: $"usermessages/get_user_messages/{messageId}/mark_read/",
                    responseHandler: response => response,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}