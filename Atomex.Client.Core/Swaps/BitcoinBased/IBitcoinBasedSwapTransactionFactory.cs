using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Bitcoin;

namespace Atomex.Swaps.BitcoinBased
{
    public interface IBitcoinBasedSwapTransactionFactory
    {
        Task<BitcoinTransaction> CreateSwapPaymentTxAsync(
            IEnumerable<BitcoinTxOutput> fromOutputs,
            BigInteger amount,
            string refundAddress,
            string toAddress,
            DateTimeOffset lockTime,
            byte[] secretHash,
            int secretSize,
            BitcoinBasedConfig currencyConfig,
            CancellationToken cancellationToken = default);

        Task<BitcoinTransaction> CreateSwapRefundTxAsync(
            BitcoinTransaction paymentTx,
            BigInteger amount,
            string refundAddress,
            byte[] redeemScript,
            DateTimeOffset lockTime,
            BitcoinBasedConfig currency);

        Task<BitcoinTransaction> CreateSwapRedeemTxAsync(
            BitcoinTransaction paymentTx,
            BigInteger amount,
            string redeemAddress,
            byte[] redeemScript,
            BitcoinBasedConfig currency,
            uint sequenceNumber = 0);
    }
}