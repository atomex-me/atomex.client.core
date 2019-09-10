using System;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.BitcoinBased;
using Atomix.Wallet.Ethereum;
using Atomix.Wallet.Tezos;

namespace Atomix.Wallet
{
    public static class CurrencyAccountCreator
    {
        public static ICurrencyAccount Create(
            Currency currency,
            IHdWallet wallet,
            IAccountDataRepository dataRepository)
        {
            switch (currency)
            {
                case BitcoinBasedCurrency _:
                    return new BitcoinBasedCurrencyAccount(
                        currency,
                        wallet,
                        dataRepository);
                case Atomix.Ethereum _:
                    return new EthereumCurrencyAccount(
                        currency,
                        wallet,
                        dataRepository);
                case Atomix.Tezos _:
                    return new TezosCurrencyAccount(
                        currency,
                        wallet,
                        dataRepository);
                default:
                    throw new NotSupportedException($"Not supported currency {currency.Name}");
            }
        }
    }
}