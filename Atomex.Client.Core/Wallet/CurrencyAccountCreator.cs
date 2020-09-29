using System;
using Atomex.Abstract;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;

namespace Atomex.Wallet
{
    public static class CurrencyAccountCreator
    {
        public static ICurrencyAccount Create(
            string currency,
            IHdWallet wallet,
            IAccountDataRepository dataRepository,
            ICurrencies currencies)
        {
            return currency switch
            {
                "BTC" => (ICurrencyAccount)new BitcoinBasedAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "LTC" => (ICurrencyAccount)new BitcoinBasedAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "USDT" => (ICurrencyAccount)new ERC20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "USDC" => (ICurrencyAccount)new ERC20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "TBTC" => (ICurrencyAccount)new ERC20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "WBTC" => (ICurrencyAccount)new ERC20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "ETH" => (ICurrencyAccount)new EthereumAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "NYX" => (ICurrencyAccount)new NYXAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "FA2" => (ICurrencyAccount)new FA2Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "TZBTC" => (ICurrencyAccount)new FA12Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "XTZ" => (ICurrencyAccount)new TezosAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                _ => throw new NotSupportedException($"Not supported currency {currency}"),
            };
        }
    }
}