using System;
using Atomex.Core.Entities;
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
            switch (currency)
            {
                case BitcoinBasedCurrency _:
                    return new BitcoinBasedCurrencyAccount(
                        currency,
                        wallet,
                        dataRepository);
                case Atomex.Ethereum _:
                    return new EthereumCurrencyAccount(
                        currency,
                        wallet,
                        dataRepository);
                case Atomex.Tezos _:
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