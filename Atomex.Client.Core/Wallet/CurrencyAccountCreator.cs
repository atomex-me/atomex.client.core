using System;
using System.Collections.Generic;

using Atomex.Abstract;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;

namespace Atomex.Wallet
{
    public static class CurrencyAccountCreator
    {
        public static IDictionary<string, ICurrencyAccount> Create(
            ICurrencies currencies,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
        {
            var accounts = new Dictionary<string, ICurrencyAccount>();

            foreach (var currency in currencies)
            {
                if (Currencies.IsTezosToken(currency.Name))
                {
                    if (!accounts.TryGetValue("XTZ", out var tezosAccount))
                    {
                        tezosAccount = CreateCurrencyAccount("XTZ", wallet, dataRepository, currencies);

                        accounts.Add("XTZ", tezosAccount);
                    }

                    accounts.Add(currency.Name, CreateCurrencyAccount(currency.Name, wallet, dataRepository, currencies, tezosAccount));
                }
                else
                {
                    accounts.Add(currency.Name, CreateCurrencyAccount(currency.Name, wallet, dataRepository, currencies));
                }
            }

            return accounts;
        }

        private static ICurrencyAccount CreateCurrencyAccount(
            string currency,
            IHdWallet wallet,
            IAccountDataRepository dataRepository,
            ICurrencies currencies,
            ICurrencyAccount baseChainAccount = null)
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
                    dataRepository,
                    baseChainAccount as TezosAccount),
                "FA2" => new FA2Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository,
                    baseChainAccount as TezosAccount),
                "TZBTC" => new FA12Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository,
                    baseChainAccount as TezosAccount),
                "XTZ" => new TezosAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "KUSD" => new FA12Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository,
                    baseChainAccount as TezosAccount),
                _ => throw new NotSupportedException($"Not supported currency {currency}"),
            };
        }
    }
}