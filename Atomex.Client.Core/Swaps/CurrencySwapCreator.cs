using System;
using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.BitcoinBased;
using Atomex.Swaps.Ethereum;
using Atomex.Swaps.Tezos;
using Atomex.Swaps.Tezos.FA12;
using Atomex.Swaps.Tezos.NYX;
using Atomex.Swaps.Tezos.FA2;
using Atomex.TezosTokens;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;

namespace Atomex.Swaps
{
    public static class CurrencySwapCreator
    {
        public static ICurrencySwap Create(
            Currency currency,
            IAccount account,
            ISwapClient swapClient)
        {
            return currency switch
            {
                BitcoinBasedCurrency _ => (ICurrencySwap)new BitcoinBasedSwap(
                       account: account.GetCurrencyAccount<BitcoinBasedAccount>(currency.Name),
                       swapClient: swapClient,
                       currencies: account.Currencies),
                ERC20 _ => (ICurrencySwap)new ERC20Swap(
                        account: account.GetCurrencyAccount<ERC20Account>(currency.Name),
                        ethereumAccount: account.GetCurrencyAccount<EthereumAccount>("ETH"),
                        swapClient: swapClient,
                        currencies: account.Currencies),
                Atomex.Ethereum _ => (ICurrencySwap)new EthereumSwap(
                        account: account.GetCurrencyAccount<EthereumAccount>(currency.Name),
                        swapClient: swapClient,
                        currencies: account.Currencies),
                NYX _ => (ICurrencySwap)new NYXSwap(
                        account: account.GetCurrencyAccount<NYXAccount>(currency.Name),
                        tezosAccount: account.GetCurrencyAccount<TezosAccount>("XTZ"),
                        swapClient: swapClient,
                        currencies: account.Currencies),
                FA2 _ => (ICurrencySwap)new FA2Swap(
                        account: account.GetCurrencyAccount<FA2Account>(currency.Name),
                        tezosAccount: account.GetCurrencyAccount<TezosAccount>("XTZ"),
                        swapClient: swapClient,
                        currencies: account.Currencies),
                FA12 _ => (ICurrencySwap)new FA12Swap(
                        account: account.GetCurrencyAccount<FA12Account>(currency.Name),
                        tezosAccount: account.GetCurrencyAccount<TezosAccount>("XTZ"),
                        swapClient: swapClient,
                        currencies: account.Currencies),
                Atomex.Tezos _ => (ICurrencySwap)new TezosSwap(
                        account: account.GetCurrencyAccount<TezosAccount>(currency.Name),
                        swapClient: swapClient,
                        currencies: account.Currencies),
                _ => throw new NotSupportedException($"Not supported currency {currency.Name}"),
            };
        }
    }
}