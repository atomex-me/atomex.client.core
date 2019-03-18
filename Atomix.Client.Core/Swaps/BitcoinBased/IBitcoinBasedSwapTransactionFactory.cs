using System;
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
            Order order,
            SwapRequisites requisites,
            byte[] secretHash,
            ITxOutputSource outputsSource);

        Task<IBitcoinBasedTransaction> CreateSwapRefundTxAsync(
            IBitcoinBasedTransaction paymentTx,
            Order order,
            DateTimeOffset lockTime);

        Task<IBitcoinBasedTransaction> CreateSwapRedeemTxAsync(
            IBitcoinBasedTransaction paymentTx,
            Order order,
            WalletAddress redeemAddress);
    }
}