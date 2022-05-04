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
        public static IDictionary<string, ICurrencyAccount_OLD> Create(
            ICurrencies currencies,
            IHdWallet_OLD wallet,
            IAccountDataRepository_OLD dataRepository)
        {
            var accounts = new Dictionary<string, ICurrencyAccount_OLD>();

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

        public static ICurrencyAccount_OLD CreateCurrencyAccount(
            string currency,
            IHdWallet_OLD wallet,
            IAccountDataRepository_OLD dataRepository,
            ICurrencies currencies,
            ICurrencyAccount_OLD baseChainAccount = null)
        {
            return currency switch
            {
                "BTC" or "LTC" => (ICurrencyAccount_OLD) new BitcoinBasedAccount_OLD(
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
                    currency: currency,
                    tokenContract: currencies
                        .Get<Fa12Config>(currency)
                        .TokenContractAddress,
                    tokenId: 0,
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

        public static ICurrencyAccount_OLD CreateTezosTokenAccount(
            string tokenType,
            string tokenContract,
            decimal tokenId,
            ICurrencies currencies,
            IHdWallet_OLD wallet,
            IAccountDataRepository_OLD dataRepository,
            TezosAccount tezosAccount)
        {
            return tokenType switch
            {
                "FA12" or "KUSD" or "TZBTC" => new Fa12Account(
                    currency: tokenType,
                    tokenContract: tokenContract,
                    tokenId: tokenId,
                    currencies: currencies,
                    wallet: wallet,
                    dataRepository: dataRepository,
                    tezosAccount: tezosAccount),

                "FA2" => new Fa2Account(
                    currency: tokenType,
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