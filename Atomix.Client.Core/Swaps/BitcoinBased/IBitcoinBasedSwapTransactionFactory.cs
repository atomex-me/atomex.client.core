using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Core.Entities;

namespace Atomix.Swaps.BitcoinBased
{
    public interface IBitcoinBasedSwapTransactionFactory
    {
        Task<IBitcoinBasedTransaction> CreateSwapPaymentTxAsync(
            BitcoinBasedCurrency currency,
            ClientSwap swap,
            IEnumerable<string> fromWallets,
            string refundAddress,
            string toAddress,
            DateTimeOffset lockTime,
            byte[] secretHash,
            int secretSize,
            ITxOutputSource outputsSource);

        Task<IBitcoinBasedTransaction> CreateSwapRefundTxAsync(
            IBitcoinBasedTransaction paymentTx,
            ClientSwap swap,
            string refundAddress,
            DateTimeOffset lockTime);

        Task<IBitcoinBasedTransaction> CreateSwapRedeemTxAsync(
            IBitcoinBasedTransaction paymentTx,
            ClientSwap swap,
            string redeemAddress);
    }
}