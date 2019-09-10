using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Common;
using Atomix.Core.Entities;
using Serilog;

namespace Atomix.Swaps.BitcoinBased
{
    public class BitcoinBasedSwapTransactionFactory : IBitcoinBasedSwapTransactionFactory
    {
        public async Task<IBitcoinBasedTransaction> CreateSwapPaymentTxAsync(
            BitcoinBasedCurrency currency,
            ClientSwap swap,
            IEnumerable<string> fromWallets,
            string refundAddress,
            string toAddress,
            DateTimeOffset lockTime,
            byte[] secretHash,
            int secretSize,
            ITxOutputSource outputsSource)
        {
            var availableOutputs = (await outputsSource
                .GetAvailableOutputsAsync(currency, fromWallets)
                .ConfigureAwait(false))
                .ToList();

            var fee = 0L;
            var orderAmount = (long)(AmountHelper.QtyToAmount(swap.Side, swap.Qty, swap.Price) *
                              currency.DigitsMultiplier);

            var requiredAmount = orderAmount + fee;

            long usedAmount;
            IList<ITxOutput> usedOutputs;
            IBitcoinBasedTransaction tx;

            do
            {
                usedOutputs = availableOutputs
                    .SelectOutputsForAmount(requiredAmount)
                    .ToList();

                usedAmount = usedOutputs.Sum(o => o.Value);

                if (usedAmount < requiredAmount)
                    throw new Exception($"Insufficient funds. Available {usedAmount}, required {requiredAmount}");

                var estimatedSigSize = EstimateSigSize(usedOutputs);

                tx = currency.CreateHtlcP2PkhSwapPaymentTx(
                    unspentOutputs: usedOutputs,
                    aliceRefundAddress: refundAddress,
                    bobAddress: toAddress,
                    lockTime: lockTime,
                    secretHash: secretHash,
                    secretSize: secretSize,
                    amount: orderAmount,
                    fee: fee);

                var txSize = tx.VirtualSize();

                fee = (long)(currency.FeeRate * (txSize + estimatedSigSize));

                requiredAmount = orderAmount + fee;

            } while (usedAmount < requiredAmount);

            tx = currency.CreateHtlcP2PkhSwapPaymentTx(
                unspentOutputs: usedOutputs,
                aliceRefundAddress: refundAddress,
                bobAddress: toAddress,
                lockTime: lockTime,
                secretHash: secretHash,
                secretSize: secretSize,
                amount: orderAmount,
                fee: fee);

            return tx;
        }

        public Task<IBitcoinBasedTransaction> CreateSwapRefundTxAsync(
            IBitcoinBasedTransaction paymentTx,
            ClientSwap swap,
            string refundAddress,
            DateTimeOffset lockTime)
        {
            var currency = (BitcoinBasedCurrency)paymentTx.Currency;
            var orderAmount = (long)(AmountHelper.QtyToAmount(swap.Side, swap.Qty, swap.Price) *
                                     currency.DigitsMultiplier);

            var swapOutputs = paymentTx.Outputs
                .Cast<BitcoinBasedTxOutput>()
                .Where(o => o.Value == orderAmount && o.IsSwapPayment)
                .ToList();

            if (swapOutputs.Count != 1)
                throw new Exception("Payment tx must have only one swap payment output");

            var estimatedSigSize = EstimateSigSize(swapOutputs, forRefund: true);

            var txSize = currency
                .CreateSwapRefundTx(
                    unspentOutputs: swapOutputs,
                    destinationAddress: refundAddress,
                    changeAddress: refundAddress,
                    amount: orderAmount,
                    fee: 0,
                    lockTime: lockTime)
                .VirtualSize();

            var fee = (long)(currency.FeeRate * (txSize + estimatedSigSize));

            if (orderAmount - fee < 0)
                throw new Exception($"Insufficient funds for fee. Available {orderAmount}, required {fee}");

            var tx = currency.CreateSwapRefundTx(
                unspentOutputs: swapOutputs,
                destinationAddress: refundAddress,
                changeAddress: refundAddress,
                amount: orderAmount - fee,
                fee: fee,
                lockTime: lockTime);

            return Task.FromResult(tx);
        }

        public Task<IBitcoinBasedTransaction> CreateSwapRedeemTxAsync(
            IBitcoinBasedTransaction paymentTx,
            ClientSwap swap,
            string redeemAddress)
        {
            var currency = (BitcoinBasedCurrency)paymentTx.Currency;
            var orderAmount = (long)(AmountHelper.QtyToAmount(swap.Side.Opposite(), swap.Qty, swap.Price) *
                                     currency.DigitsMultiplier);

            var swapOutputs = paymentTx
                .SwapOutputs()
                .ToList();

            if (swapOutputs.Count != 1)
                throw new Exception("Payment tx must have only one swap payment output");

            var estimatedSigSize = EstimateSigSize(swapOutputs);

            var txSize = currency
                .CreateP2PkhTx(
                    unspentOutputs: swapOutputs,
                    destinationAddress: redeemAddress,
                    changeAddress: redeemAddress,
                    amount: orderAmount,
                    fee: 0)
                .VirtualSize();

            var fee = (long)(currency.FeeRate * (txSize + estimatedSigSize));

            if (orderAmount - fee < 0)
                throw new Exception($"Insufficient funds for fee. Available {orderAmount}, required {fee}");

            var tx = currency.CreateP2PkhTx(
                unspentOutputs: swapOutputs,
                destinationAddress: redeemAddress,
                changeAddress: redeemAddress,
                amount: orderAmount - fee,
                fee: fee);

            return Task.FromResult(tx);
        }

        private static long EstimateSigSize(IEnumerable<ITxOutput> outputs, bool forRefund = false)
        {
            var result = 0L;

            foreach (var output in outputs.Cast<BitcoinBasedTxOutput>())
            {
                if (output.IsP2Pkh)
                    result += BitcoinBasedCurrency.P2PkhScriptSigSize; // use compressed?
                else if (output.IsSegwitP2Pkh)
                    result += BitcoinBasedCurrency.P2WPkhScriptSigSize;
                else if (output.IsP2PkhSwapPayment || output.IsHtlcP2PkhSwapPayment)
                    result += forRefund
                        ? BitcoinBasedCurrency.P2PkhSwapRefundSigSize
                        : BitcoinBasedCurrency.P2PkhSwapRedeemSigSize;
                else
                    Log.Warning("Unknown output type, estimated fee may be wrong");
            }

            return result;
        }
    }
}