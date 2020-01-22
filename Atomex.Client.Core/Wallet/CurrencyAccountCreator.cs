using System;
using Atomex.Core;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;

namespace Atomex.Wallet
{
    public static class CurrencyAccountCreator
    {
        public static ICurrencyAccount Create(
            Currency currency,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
        {
            return currency switch
            {
                BitcoinBasedCurrency _ => (ICurrencyAccount)new BitcoinBasedAccount(
                       currency,
                       wallet,
                       dataRepository),
                Atomex.Ethereum _ => (ICurrencyAccount)new EthereumAccount(
                        currency,
                        wallet,
                        dataRepository),
                Atomex.Tezos _ => (ICurrencyAccount)new TezosAccount(
                        currency,
                        wallet,
                        dataRepository),
                _ => throw new NotSupportedException($"Not supported currency {currency.Name}"),
            };
        }
    }
}