using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.BitcoinBased;

namespace Atomex.Swaps.BitcoinBased
{
    public interface IBitcoinBasedSwapTransactionFactory
    {
        Task<IBitcoinBasedTransaction_OLD> CreateSwapPaymentTxAsync(
            IEnumerable<BitcoinBasedTxOutput> fromOutputs,
            long amount,
            string refundAddress,
            string toAddress,
            DateTimeOffset lockTime,
            byte[] secretHash,
            int secretSize,
            BitcoinBasedConfig currencyConfig,
            CancellationToken cancellationToken = default);

        Task<IBitcoinBasedTransaction_OLD> CreateSwapRefundTxAsync(
            IBitcoinBasedTransaction_OLD paymentTx,
            long amount,
            string refundAddress,
            byte[] redeemScript,
            DateTimeOffset lockTime,
            BitcoinBasedConfig currency);

        Task<IBitcoinBasedTransaction_OLD> CreateSwapRedeemTxAsync(
            IBitcoinBasedTransaction_OLD paymentTx,
            long amount,
            string redeemAddress,
            byte[] redeemScript,
            BitcoinBasedConfig currency,
            uint sequenceNumber = 0);
    }
}