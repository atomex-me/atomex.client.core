using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;

namespace Atomex.Swaps.BitcoinBased
{
    public interface IBitcoinBasedSwapTransactionFactory
    {
        Task<IBitcoinBasedTransaction> CreateSwapPaymentTxAsync(
            BitcoinBasedConfig currency,
            long amount,
            IEnumerable<string> fromWallets,
            string refundAddress,
            string toAddress,
            DateTimeOffset lockTime,
            byte[] secretHash,
            int secretSize,
            ITxOutputSource outputsSource);

        Task<IBitcoinBasedTransaction> CreateSwapRefundTxAsync(
            BitcoinBasedConfig currency,
            IBitcoinBasedTransaction paymentTx,
            long amount,
            string refundAddress,
            DateTimeOffset lockTime,
            byte[] redeemScript);

        Task<IBitcoinBasedTransaction> CreateSwapRedeemTxAsync(
            BitcoinBasedConfig currency,
            IBitcoinBasedTransaction paymentTx,
            long amount,
            string redeemAddress,
            byte[] redeemScript,
            uint sequenceNumber = 0);
    }
}