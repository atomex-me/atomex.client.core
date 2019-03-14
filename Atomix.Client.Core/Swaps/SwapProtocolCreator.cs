using System;
using Atomix.Common.Abstract;
using Atomix.Core.Entities;
using Atomix.Swaps.Abstract;
using Atomix.Swaps.BitcoinBased;
using Atomix.Swaps.Ethereum;
using Atomix.Wallet.Abstract;

namespace Atomix.Swaps
{
    public class SwapProtocolCreator
    {
        public static ISwap Create(
            Currency currency,
            SwapState swapState,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer)
        {
            switch (currency)
            {
                case BitcoinBasedCurrency _:
                    return new BitcoinBasedSwap(
                        currency: currency,
                        swapState: swapState,
                        account: account,
                        swapClient: swapClient,
                        taskPerformer: taskPerformer);
                case Atomix.Ethereum _:
                    return new EthereumSwap(
                        currency: currency,
                        swapState: swapState,
                        account: account,
                        swapClient: swapClient,
                        taskPerformer: taskPerformer);
                default:
                    throw new NotSupportedException($"Not supported currency {currency.Name}");
            }
        }
    }
}