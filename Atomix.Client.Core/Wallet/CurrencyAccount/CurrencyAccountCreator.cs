using System;
using Atomix.Blockchain.Abstract;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;

namespace Atomix.Wallet.CurrencyAccount
{
    public class CurrencyAccountCreator
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
                case Ethereum _:
                    return new EthereumCurrencyAccount(
                        currency,
                        wallet,
                        transactionRepository);
                case Tezos _:
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