using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Bitcoin;

namespace Atomex.Swaps.BitcoinBased
{
    public interface IBitcoinBasedSwapTransactionFactory
    {
        Task<BitcoinTransaction> CreateSwapPaymentTxAsync(
            IEnumerable<BitcoinTxOutput> fromOutputs,
            long amount,
            string refundAddress,
            string toAddress,
            DateTimeOffset lockTime,
            byte[] secretHash,
            int secretSize,
            BitcoinBasedConfig currencyConfig,
            CancellationToken cancellationToken = default);

        Task<BitcoinTransaction> CreateSwapRefundTxAsync(
            BitcoinTransaction paymentTx,
            long amount,
            string refundAddress,
            byte[] redeemScript,
            DateTimeOffset lockTime,
            BitcoinBasedConfig currency);

        Task<BitcoinTransaction> CreateSwapRedeemTxAsync(
            BitcoinTransaction paymentTx,
            long amount,
            string redeemAddress,
            byte[] redeemScript,
            BitcoinBasedConfig currency,
            uint sequenceNumber = 0);
    }
}