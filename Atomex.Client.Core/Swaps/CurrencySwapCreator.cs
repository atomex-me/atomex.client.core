using System;
using Atomex.Common.Abstract;
using Atomex.Core.Entities;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.BitcoinBased;
using Atomex.Swaps.Ethereum;
using Atomex.Swaps.Tezos;
using Atomex.Wallet.Abstract;

namespace Atomex.Swaps
{
    public static class CurrencySwapCreator
    {
        public static ICurrencySwap Create(
            Currency currency,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer)
        {
            switch (currency)
            {
                case BitcoinBasedCurrency _:
                    return new BitcoinBasedSwap(
                        currency: currency,
                        account: account,
                        swapClient: swapClient,
                        taskPerformer: taskPerformer,
                        transactionFactory: new BitcoinBasedSwapTransactionFactory());
                case Atomex.Ethereum _:
                    return new EthereumSwap(
                        currency: currency,
                        account: account,
                        swapClient: swapClient,
                        taskPerformer: taskPerformer);
                case Atomex.Tezos _:
                    return new TezosSwap(
                        currency: currency,
                        account: account,
                        swapClient: swapClient,
                        taskPerformer: taskPerformer);
                default:
                    throw new NotSupportedException($"Not supported currency {currency.Name}");
            }
        }
    }
}