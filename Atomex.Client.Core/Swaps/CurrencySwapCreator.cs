using System;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.BitcoinBased;
using Atomex.Swaps.Ethereum;
using Atomex.Swaps.Tezos;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;

namespace Atomex.Swaps
{
    public static class CurrencySwapCreator
    {
        public static ICurrencySwap Create(
            Currency currency,
            IAccount account,
            ISwapClient swapClient)
        {
            return currency switch
            {
                BitcoinBasedCurrency _ => (ICurrencySwap)new BitcoinBasedSwap(
                       account: account.GetCurrencyAccount<BitcoinBasedAccount>(currency.Name),
                       swapClient: swapClient,
                       transactionFactory: new BitcoinBasedSwapTransactionFactory()),
                Atomex.Ethereum _ => (ICurrencySwap)new EthereumSwap(
                        account: account.GetCurrencyAccount<EthereumAccount>(currency.Name),
                        swapClient: swapClient),
                Atomex.Tezos _ => (ICurrencySwap)new TezosSwap(
                        account: account.GetCurrencyAccount<TezosAccount>(currency.Name),
                        swapClient: swapClient),
                _ => throw new NotSupportedException($"Not supported currency {currency.Name}"),
            };
        }
    }
}