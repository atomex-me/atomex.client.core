using System;
using System.Collections.Generic;

using Atomex.Abstract;
using Atomex.TezosTokens;
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
            ILocalStorage dataRepository)
        {
            var accounts = new Dictionary<string, ICurrencyAccount>();

            foreach (var currency in currencies)
            {
                if (Currencies.IsTezosToken(currency.Name))
                {
                    if (!accounts.TryGetValue(TezosConfig.Xtz, out var tezosAccount))
                    {
                        tezosAccount = CreateCurrencyAccount(
                            currency: TezosConfig.Xtz,
                            wallet: wallet,
                            dataRepository: dataRepository,
                            currencies: currencies);

                        accounts.Add(TezosConfig.Xtz, tezosAccount);
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

        public static ICurrencyAccount CreateCurrencyAccount(
            string currency,
            IHdWallet wallet,
            ILocalStorage dataRepository,
            ICurrencies currencies,
            ICurrencyAccount baseChainAccount = null)
        {
            return currency switch
            {
                "BTC" or "LTC" => (ICurrencyAccount) new BitcoinBasedAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),

                "USDT" or "USDC" or "TBTC" or "WBTC" => new Erc20Account(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),

                "ETH" => new EthereumAccount(
                    currency,
                    currencies,
                    wallet,
                    dataRepository),

                "TZBTC" or "KUSD" => new Fa12Account(
                    tokenContract: currencies
                        .Get<Fa12Config>(currency)
                        .TokenContractAddress,
                    tokenId: 0,
                    currencies: currencies,
                    wallet: wallet,
                    dataRepository: dataRepository,
                    tezosAccount: baseChainAccount as TezosAccount),

                "USDT_XTZ" => new Fa2Account(
                    tokenContract: currencies
                        .Get<Fa2Config>(currency)
                        .TokenContractAddress,
                    tokenId: currencies
                        .Get<Fa2Config>(currency)
                        .TokenId,
                    currencies: currencies,
                    wallet: wallet,
                    dataRepository: dataRepository,
                    tezosAccount: baseChainAccount as TezosAccount),

                "XTZ" => new TezosAccount(
                    currencies,
                    wallet,
                    dataRepository),

                _ => throw new NotSupportedException($"Not supported currency {currency}."),
            };
        }

        public static ICurrencyAccount CreateTezosTokenAccount(
            string tokenType,
            string tokenContract,
            int tokenId,
            ICurrencies currencies,
            IHdWallet wallet,
            ILocalStorage dataRepository,
            TezosAccount tezosAccount)
        {
            return tokenType switch
            {
                "FA12" or "KUSD" or "TZBTC" => new Fa12Account(
                    tokenContract: tokenContract,
                    tokenId: tokenId,
                    currencies: currencies,
                    wallet: wallet,
                    dataRepository: dataRepository,
                    tezosAccount: tezosAccount),

                "FA2" or "USDT_XTZ" => new Fa2Account(
                    tokenContract: tokenContract,
                    tokenId: tokenId,
                    currencies: currencies,
                    wallet: wallet,
                    dataRepository: dataRepository,
                    tezosAccount: tezosAccount),

                _ => throw new NotSupportedException($"Not supported token type {tokenType}."),
            };
        }
    }
}