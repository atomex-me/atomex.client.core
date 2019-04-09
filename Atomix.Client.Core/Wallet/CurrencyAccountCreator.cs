using System;
using Atomix.Blockchain.Abstract;
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
            ITransactionRepository transactionRepository)
        {
            switch (currency)
            {
                case BitcoinBasedCurrency _:
                    return new BitcoinBasedCurrencyAccount(
                        currency,
                        wallet,
                        transactionRepository);
                case Atomix.Ethereum _:
                    return new EthereumCurrencyAccount(
                        currency,
                        wallet,
                        transactionRepository);
                case Atomix.Tezos _:
                    return new TezosCurrencyAccount(
                        currency,
                        wallet,
                        transactionRepository);
                default:
                    throw new NotSupportedException($"Not supported currency {currency.Name}");
            }
        }
    }
}