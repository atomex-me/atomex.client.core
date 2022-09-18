﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography;
using Atomex.Services.Abstract;
using Atomex.Wallet.Abstract;
using Newtonsoft.Json;

namespace Atomex.ViewModels
{
    public static class Helpers
    {
        public class SwapPaymentParams
        {
            public decimal Amount { get; set; }
            public decimal PaymentFee { get; set; }
            public decimal MakerNetworkFee { get; set; }
            public decimal ReservedForSwaps { get; set; }
            public Error Error { get; set; }
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
            public decimal TargetAmount { get; set; }
            public decimal OrderPrice { get; set; }
            public decimal Price { get; set; }
            public decimal MaxAmount { get; set; }
            public bool IsNoLiquidity { get; set; }
        }


        public static IEnumerable<SwapDetailingInfo> GetSwapDetailingInfo(Swap swap, IAccount account)
        {
            var soldCurrencyConfig = account.Currencies.GetByName(swap.SoldCurrency);
            var purchaseCurrencyConfig = account.Currencies.GetByName(swap.PurchasedCurrency);
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

        public static Task<SwapPaymentParams> EstimateSwapPaymentParamsAsync(
            decimal amount,
            CurrencyConfig fromCurrency,
            CurrencyConfig toCurrency,
            IAccount account,
            IAtomexClient atomexClient,
            ISymbolsProvider symbolsProvider,
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
                        MakerNetworkFee = 0,
                        ReservedForSwaps = 0,
                        Error = null
                    };
                }

                var fromCurrencyAccount = account
                    .GetCurrencyAccount<ILegacyCurrencyAccount>(fromCurrency.Name);

                // estimate max payment amount and max fee
                var (maxAmount, maxFee, _) = await fromCurrencyAccount
                    .EstimateMaxAmountToSendAsync(
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

                // estimate maker network fee
                var estimatedMakerNetworkFee = await EstimateMakerNetworkFeeAsync(
                        fromCurrency: fromCurrency,
                        toCurrency: toCurrency,
                        account: account,
                        atomexClient: atomexClient,
                        symbolsProvider: symbolsProvider,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var hasSameChainForFees = fromCurrency.FeeCurrencyName == fromCurrency.Name;

                var maxNetAmount = Math.Max(maxAmount - reservedForSwapsAmount - estimatedMakerNetworkFee, 0m);

                if (maxNetAmount == 0m)
                {
                    return new SwapPaymentParams
                    {
                        Amount = 0m,
                        PaymentFee = 0m,
                        MakerNetworkFee = 0m,
                        ReservedForSwaps = 0m,
                        Error = hasSameChainForFees
                            ? new Error(Errors.InsufficientFunds, "Insufficient funds to cover fees")
                            : new Error(Errors.InsufficientChainFunds,
                                string.Format(CultureInfo.InvariantCulture,
                                    "Insufficient {0} to cover token transfer fee", fromCurrency.FeeCurrencyName))
                    };
                }

                if (amount > maxNetAmount) // amount greater than max net amount
                {
                    return new SwapPaymentParams
                    {
                        Amount = Math.Max(maxNetAmount, 0m),
                        PaymentFee = maxFee,
                        MakerNetworkFee = estimatedMakerNetworkFee,
                        ReservedForSwaps = reservedForSwapsAmount,
                        Error = null
                    };
                }

                var estimatedPaymentFee = await fromCurrencyAccount
                    .EstimateFeeAsync(
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
                        MakerNetworkFee = 0m,
                        ReservedForSwaps = 0m,
                        Error = hasSameChainForFees
                            ? new Error(Errors.InsufficientFunds, "Insufficient funds to cover fees")
                            : new Error(Errors.InsufficientChainFunds,
                                string.Format(CultureInfo.InvariantCulture,
                                    "Insufficient {0} to cover token transfer fee", fromCurrency.FeeCurrencyName))
                    };
                }

                return new SwapPaymentParams
                {
                    Amount = amount,
                    PaymentFee = estimatedPaymentFee.Value,
                    MakerNetworkFee = estimatedMakerNetworkFee,
                    ReservedForSwaps = reservedForSwapsAmount,
                    Error = null
                };
            }, cancellationToken);
        }

        public static Task<SwapPriceEstimation> EstimateSwapPriceAsync(
            decimal amount,
            CurrencyConfig fromCurrency,
            CurrencyConfig toCurrency,
            IAccount account,
            IAtomexClient atomexClient,
            ISymbolsProvider symbolsProvider,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
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

                var walletAddress = await account
                    .GetCurrencyAccount<ILegacyCurrencyAccount>(toCurrency.FeeCurrencyName)
                    .GetRedeemAddressAsync();

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
            CurrencyConfig currency)
        {
            var swaps = await account
                .GetSwapsAsync()
                .ConfigureAwait(false);

            var reservedAmount = swaps.Sum(s =>
                (s.IsActive && s.SoldCurrency == currency.Name &&
                 !s.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
                    ? (s.Symbol.IsBaseCurrency(currency.Name) ? s.Qty : s.Qty * s.Price) + s.MakerNetworkFee
                    : 0);

            return AmountHelper.RoundDown(reservedAmount, currency.DigitsMultiplier);
        }

        public static async Task<decimal> EstimateMakerNetworkFeeAsync(
            CurrencyConfig fromCurrency,
            CurrencyConfig toCurrency,
            IAccount account,
            IAtomexClient atomexClient,
            ISymbolsProvider symbolsProvider,
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
                makerRedeemFee = ConvertAmount(
                    amount: makerRedeemFee,
                    from: fromCurrency.FeeCurrencyName,
                    to: fromCurrency.Name,
                    account: account,
                    atomexClient: atomexClient,
                    symbolsProvider: symbolsProvider) ?? 0;

            // convert makerPaymentFee from toCurrency to fromCurrency
            makerPaymentFee = ConvertAmount(
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
            IAccount account,
            IAtomexClient atomexClient,
            ISymbolsProvider symbolsProvider)
        {
            var symbol = symbolsProvider
                .GetSymbols(account.Network)
                .SymbolByCurrencies(from, to);

            var toCurrency = account.Currencies.GetByName(to);

            if (symbol == null)
                throw new Exception($"Can't find symbol for {from} and {to}");

            var quote = atomexClient
                .GetOrderBook(symbol)
                ?.TopOfBook();

            if (quote == null)
                return null;

            var middlePrice = quote.GetMiddlePrice();

            return symbol.IsBaseCurrency(from)
                ? AmountHelper.RoundDown(amount * middlePrice, toCurrency.DigitsMultiplier)
                : AmountHelper.RoundDown(amount / middlePrice, toCurrency.DigitsMultiplier);
        }

        public static string GetUserId(IAccount account)
        {
            using var servicePublicKey =
                account.Wallet.GetServicePublicKey(account.UserSettings.AuthenticationKeyIndex);
            using var publicKey = servicePublicKey.ToUnsecuredBytes();
            return Sha256.Compute(Sha256.Compute(publicKey)).ToHexString();
        }

        public static async Task<List<UserMessage>> GetUserMessages(string userId,
            CancellationToken cancellationToken = default)
        {
            return await HttpHelper.GetAsync(
                    baseUri: "https://services.atomex.me/",
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
                    baseUri: "https://services.atomex.me/",
                    content: null,
                    requestUri: $"usermessages/get_user_messages/{messageId}/mark_read/",
                    responseHandler: response => response,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}