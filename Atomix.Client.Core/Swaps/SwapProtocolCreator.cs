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
        public static ISwapProtocol Create(
            Currency currency,
            Swap swap,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer,
            OnSwapUpdatedDelegate onSwapUpdated)
        {
            switch (currency)
            {
                case BitcoinBasedCurrency _:
                    return new BitcoinBasedSwapProtocol(
                        currency: currency,
                        swap: swap,
                        account: account,
                        swapClient: swapClient,
                        taskPerformer: taskPerformer,
                        onSwapUpdated: onSwapUpdated);
                case Atomix.Ethereum _:
                    return new EthereumSwapProtocol(
                        currency: currency,
                        swap: swap,
                        account: account,
                        swapClient: swapClient,
                        taskPerformer: taskPerformer,
                        onSwapUpdated: onSwapUpdated);
                default:
                    throw new NotSupportedException($"Not supported currency {currency.Name}");
            }
        }
    }
}