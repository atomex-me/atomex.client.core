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
                "LTC" => new BitcoinBasedAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "USDT" => new ERC20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "USDC" => new ERC20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "TBTC" => new ERC20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "WBTC" => new ERC20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "ETH" => new EthereumAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "NYX" => new NYXAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "FA2" => new FA2Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "TZBTC" => new FA12Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "XTZ" => new TezosAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "KUSD" => new FA12Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                _ => throw new NotSupportedException($"Not supported currency {currency}"),
            };
        }
    }
}