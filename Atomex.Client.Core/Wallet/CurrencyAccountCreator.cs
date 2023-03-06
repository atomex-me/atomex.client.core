using System;
using System.Numerics;

using Atomex.Abstract;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;

namespace Atomex.Wallet
{
    public static class CurrencyAccountCreator
    {
        public static ICurrencyAccount CreateCurrencyAccount(
            string currency,
            IHdWallet wallet,
            ILocalStorage localStorage,
            ICurrencies currencies,
            string tokenContract = null,
            BigInteger? tokenId = null,
            ICurrencyAccount baseChainAccount = null)
        {
            return currency switch
            {
                "BTC" or "LTC" => new BitcoinBasedAccount(
                    currency: currency,
                    currencies: currencies,
                    wallet: wallet,
                    localStorage: localStorage),

                //"USDT" or "USDC" or "TBTC" or "WBTC" => new Erc20Account(
                //    tokenContract: currencies
                //        .Get<Erc20Config>(currency)
                //        .TokenContractAddress,
                //    currencies: currencies,
                //    wallet: wallet,
                //    localStorage: localStorage,
                //    ethereumAccount: baseChainAccount as EthereumAccount),

                "ERC20" => new Erc20Account(
                    tokenContract: tokenContract,
                    currencies: currencies,
                    wallet: wallet,
                    localStorage: localStorage,
                    ethereumAccount: baseChainAccount as EthereumAccount),

                "ETH" => new EthereumAccount(
                    currency: currency,
                    currencies: currencies,
                    wallet: wallet,
                    localStorage: localStorage),

                //"TZBTC" or "KUSD" => new Fa12Account(
                //    tokenContract: currencies
                //        .Get<Fa12Config>(currency)
                //        .TokenContractAddress,
                //    tokenId: 0,
                //    currencies: currencies,
                //    wallet: wallet,
                //    localStorage: localStorage,
                //    tezosAccount: baseChainAccount as TezosAccount),

                "FA12" => new Fa12Account(
                    tokenContract: tokenContract,
                    tokenId: 0,
                    currencies: currencies,
                    wallet: wallet,
                    localStorage: localStorage,
                    tezosAccount: baseChainAccount as TezosAccount),

                //"USDT_XTZ" => new Fa2Account(
                //    tokenContract: currencies
                //        .Get<Fa2Config>(currency)
                //        .TokenContractAddress,
                //    tokenId: currencies
                //        .Get<Fa2Config>(currency)
                //        .TokenId,
                //    currencies: currencies,
                //    wallet: wallet,
                //    localStorage: localStorage,
                //    tezosAccount: baseChainAccount as TezosAccount),

                "FA2" => new Fa2Account(
                    tokenContract: tokenContract,
                    tokenId: tokenId.Value,
                    currencies: currencies,
                    wallet: wallet,
                    localStorage: localStorage,
                    tezosAccount: baseChainAccount as TezosAccount),

                "XTZ" => new TezosAccount(
                    currencies: currencies,
                    wallet: wallet,
                    localStorage: localStorage),

                _ => throw new NotSupportedException($"Not supported currency {currency}"),
            };
        }
    }
}