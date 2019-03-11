using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Common;
using Atomix.Core.Entities;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Atomix.Swaps.BitcoinBased
{
    public static class BitcoinBasedSwapTransactionFactory
    {
        public static async Task<IBitcoinBasedTransaction> CreateSwapPaymentTxAsync(
            BitcoinBasedCurrency currency,
            Order order,
            SwapRequisites requisites,
            byte[] secretHash,
            ITxOutputSource outputsSource)
        {
            IBitcoinBasedTransaction tx;

            var fee = 0L;
            long usedAmount;
            var orderAmount = (long)(AmountHelper.QtyToAmount(order.Side, order.LastQty, order.LastPrice) *
                              currency.DigitsMultiplier);
            var requiredAmount = orderAmount + fee;

            var unspentOutputs = await outputsSource
                .GetUnspentOutputsAsync(order.FromWallets)
                .ConfigureAwait(false);

            var unspentOutputsList = unspentOutputs.ToList();
            IList<ITxOutput> usedOutputs;

            do
            {
                usedOutputs = unspentOutputsList
                    .SelectOutputsForAmount(requiredAmount)
                    .ToList();

                usedAmount = usedOutputs.Sum(o => o.Value);

                if (usedAmount < requiredAmount)
                    throw new Exception($"Insufficient funds. Available {usedAmount}, required {requiredAmount}");

                var estimatedSigSize = usedOutputs.EstimateSigSize(); //EstimateSigSize(usedOutputs);

                tx = currency.CreateP2PkhSwapPaymentTx(
                    unspentOutputs: usedOutputs,
                    aliceRefundPubKey: order.RefundWallet.PublicKeyBytes(),
                    bobRefundPubKey: requisites.ToWallet.PublicKeyBytes(),
                    bobAddress: requisites.ToWallet.Address,
                    secretHash: secretHash,
                    amount: orderAmount,
                    fee: fee);

                var txSize = tx.VirtualSize();

                fee = (long)(currency.FeeRate * (txSize + estimatedSigSize));

                requiredAmount = orderAmount + fee;

            } while (usedAmount < requiredAmount);

            tx = currency.CreateP2PkhSwapPaymentTx(
                unspentOutputs: usedOutputs,
                aliceRefundPubKey: order.RefundWallet.PublicKeyBytes(),
                bobRefundPubKey: requisites.ToWallet.PublicKeyBytes(),
                bobAddress: requisites.ToWallet.Address,
                secretHash: secretHash,
                amount: orderAmount,
                fee: fee);

            return tx;
        }

        public static Task<IBitcoinBasedTransaction> CreateSwapRefundTxAsync(
            this IBitcoinBasedTransaction paymentTx,
            Order order,
            DateTimeOffset lockTime)
        {
            var currency = (BitcoinBasedCurrency)paymentTx.Currency;
            var orderAmount = (long)(AmountHelper.QtyToAmount(order.Side, order.LastQty, order.LastPrice) *
                                     currency.DigitsMultiplier);

            var swapOutputs = paymentTx.Outputs
                .Cast<BitcoinBasedTxOutput>()
                .Where(o => o.Value == orderAmount && o.IsP2PkhSwapPayment)
                .ToList();

            if (swapOutputs.Count != 1)
                throw new Exception("Payment tx must have only one swap payment output");

            var estimatedSigSize = swapOutputs.EstimateSigSize(forRefund: true); //  EstimateSigSize(swapOutputs, forRefund: true);

            var txSize = currency
                .CreateSwapRefundTx(
                    unspentOutputs: swapOutputs,
                    destinationAddress: order.RefundWallet.Address,
                    changeAddress: order.RefundWallet.Address,
                    amount: orderAmount,
                    fee: 0,
                    lockTime: lockTime)
                .VirtualSize();

            var fee = (long)(currency.FeeRate * (txSize + estimatedSigSize));

            if (orderAmount - fee < 0)
                throw new Exception($"Insufficient funds for fee. Available {orderAmount}, required {fee}");

            var tx = currency.CreateSwapRefundTx(
                unspentOutputs: swapOutputs,
                destinationAddress: order.RefundWallet.Address,
                changeAddress: order.RefundWallet.Address,
                amount: orderAmount - fee,
                fee: fee,
                lockTime: lockTime);

            return Task.FromResult(tx);
        }

        public static Task<IBitcoinBasedTransaction> CreateSwapRedeemTxAsync(
            this IBitcoinBasedTransaction paymentTx,
            Order order,
            WalletAddress redeemAddress)
        {
            var currency = (BitcoinBasedCurrency)paymentTx.Currency;
            var orderAmount = (long)(AmountHelper.QtyToAmount(order.Side.Opposite(), order.LastQty, order.LastPrice) *
                                     currency.DigitsMultiplier);

            var swapOutputs = paymentTx
                .SwapOutputs()
                .Where(o => o.IsSwapPayment) //o.Value == orderAmount)
                .ToList();

            if (swapOutputs.Count != 1)
                throw new Exception("Payment tx must have only one swap payment output");

            var estimatedSigSize = swapOutputs.EstimateSigSize(); //EstimateSigSize(swapOutputs);

            var txSize = currency
                .CreateP2PkhTx(
                    unspentOutputs: swapOutputs,
                    destinationAddress: redeemAddress.Address,
                    changeAddress: redeemAddress.Address,
                    amount: orderAmount,
                    fee: 0)
                .VirtualSize();

            var fee = (long)(currency.FeeRate * (txSize + estimatedSigSize));

            if (orderAmount - fee < 0)
                throw new Exception($"Insufficient funds for fee. Available {orderAmount}, required {fee}");

            var tx = currency.CreateP2PkhTx(
                unspentOutputs: swapOutputs,
                destinationAddress: redeemAddress.Address,
                changeAddress: redeemAddress.Address,
                amount: orderAmount - fee,
                fee: fee);

            return Task.FromResult(tx);
        }

        private static long EstimateSigSize(
            this IEnumerable<ITxOutput> outputs,
            bool forRefund = false)
        {
            var result = 0L;

            foreach (var output in outputs.Cast<BitcoinBasedTxOutput>())
            {
                if (output.IsP2Pkh)
                    result += BitcoinBasedCurrency.P2PkhScriptSigSize; // use compressed?
                else if (output.IsSegwitP2Pkh)
                    result += BitcoinBasedCurrency.P2WPkhScriptSigSize;
                else if (output.IsP2PkhSwapPayment)
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