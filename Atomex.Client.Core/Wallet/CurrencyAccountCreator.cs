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
                        tezosAccount = CreateCurrencyAccount(
                            currency: "XTZ",
                            wallet: wallet,
                            dataRepository: dataRepository,
                            currencies: currencies);

                        accounts.Add("XTZ", tezosAccount);
                    }

                    accounts.Add(currency.Name, CreateCurrencyAccount(
                        currency: currency.Name,
                        wallet: wallet,
                        dataRepository: dataRepository,
                        currencies: currencies,
                        baseChainAccount: tezosAccount));
                }
                else
                {
                    accounts.Add(currency.Name, CreateCurrencyAccount(
                        currency: currency.Name,
                        wallet: wallet,
                        dataRepository: dataRepository,
                        currencies: currencies));
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
                "USDT" => new Erc20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "USDC" => new Erc20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "TBTC" => new Erc20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "WBTC" => new Erc20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "ETH" => new EthereumAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),
                "FA2" => new Fa2Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository,
                    baseChainAccount as TezosAccount),
                "TZBTC" => new Fa12Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository,
                    baseChainAccount as TezosAccount),
                "XTZ" => new TezosAccount(
                    currencies,
                    wallet,
                    dataRepository),
                "KUSD" => new Fa12Account(
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