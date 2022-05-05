using System;

using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.BitcoinBased;
using Atomex.Swaps.Ethereum;
using Atomex.Swaps.Tezos;
using Atomex.Swaps.Tezos.FA12;
using Atomex.TezosTokens;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;

namespace Atomex.Swaps
{
    public static class CurrencySwapCreator
    {
        public static ICurrencySwap Create(
            CurrencyConfig_OLD currency,
            IAccount_OLD account)
        {
            return currency switch
            {
                BitcoinBasedConfig_OLD _ => new BitcoinBasedSwap(
                    account: account.GetCurrencyAccount<BitcoinBasedAccount_OLD>(currency.Name),
                    currencies: account.Currencies),

                Erc20Config _ => new Erc20Swap(
                    account: account.GetCurrencyAccount<Erc20Account>(currency.Name),
                    ethereumAccount: account.GetCurrencyAccount<EthereumAccount>("ETH"),
                    currencies: account.Currencies),

                EthereumConfig_ETH _ => new EthereumSwap(
                    account: account.GetCurrencyAccount<EthereumAccount>(currency.Name),
                    currencies: account.Currencies),

                Fa12Config _ => new Fa12Swap(
                    account: account.GetCurrencyAccount<Fa12Account>(currency.Name),
                    tezosAccount: account.GetCurrencyAccount<TezosAccount>(TezosConfig_OLD.Xtz),
                    currencies: account.Currencies),

                TezosConfig_OLD _ => new TezosSwap(
                    account:   account.GetCurrencyAccount<TezosAccount>(currency.Name),
                    currencies: account.Currencies),

                _ => throw new NotSupportedException($"Not supported currency {currency.Name}")
            };
        }
    }
}