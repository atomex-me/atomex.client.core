using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;

namespace Atomex.Swaps.BitcoinBased
{
    public class BitcoinBasedSwapTransactionFactory : IBitcoinBasedSwapTransactionFactory
    {
        public async Task<(IBitcoinBasedTransaction, byte[])> CreateSwapPaymentTxAsync(
            BitcoinBasedCurrency currency,
            long amount,
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
            var requiredAmount = amount + fee;

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

                var estimatedSigSize = BitcoinBasedCurrency.EstimateSigSize(usedOutputs);

                tx = currency.CreateHtlcP2PkhScriptSwapPaymentTx(
                    unspentOutputs: usedOutputs,
                    aliceRefundAddress: refundAddress,
                    bobAddress: toAddress,
                    lockTime: lockTime,
                    secretHash: secretHash,
                    secretSize: secretSize,
                    amount: amount,
                    fee: fee,
                    redeemScript: out _);

                // fee = txSize * feeRate + dust
                fee = (long)((tx.VirtualSize() + estimatedSigSize) * currency.FeeRate) + tx.GetDust();

                requiredAmount = amount + fee;

            } while (usedAmount < requiredAmount);

            tx = currency.CreateHtlcP2PkhScriptSwapPaymentTx(
                unspentOutputs: usedOutputs,
                aliceRefundAddress: refundAddress,
                bobAddress: toAddress,
                lockTime: lockTime,
                secretHash: secretHash,
                secretSize: secretSize,
                amount: amount,
                fee: fee,
                redeemScript: out var redeemScript);

            return (tx, redeemScript);
        }

        public Task<IBitcoinBasedTransaction> CreateSwapRefundTxAsync(
            IBitcoinBasedTransaction paymentTx,
            long amount,
            string refundAddress,
            DateTimeOffset lockTime,
            byte[] redeemScript)
        {
            var currency = (BitcoinBasedCurrency)paymentTx.Currency;

            var swapOutput = paymentTx.Outputs
                .Cast<BitcoinBasedTxOutput>()
                .FirstOrDefault(o => o.IsPayToScriptHash(redeemScript));

            if (swapOutput == null)
                throw new Exception("Can't find pay to script hash output");

            var estimatedSigSize = BitcoinBasedCurrency.EstimateSigSize(swapOutput, forRefund: true);

            var tx = currency.CreateP2PkhTx(
                unspentOutputs: new ITxOutput[] { swapOutput },
                destinationAddress: refundAddress,
                changeAddress: refundAddress,
                amount: amount,
                fee: 0,
                lockTime: lockTime);

            // fee = txSize * feeRate without dust, because all coins will be send to one address
            var fee = (long) ((tx.VirtualSize() + estimatedSigSize) * currency.FeeRate);

            if (amount - fee < 0)
                throw new Exception($"Insufficient funds for fee. Available {amount}, required {fee}");

            tx = currency.CreateP2PkhTx(
                unspentOutputs: new ITxOutput[] { swapOutput },
                destinationAddress: refundAddress,
                changeAddress: refundAddress,
                amount: amount - fee,
                fee: fee,
                lockTime: lockTime);

            return Task.FromResult(tx);
        }

        public Task<IBitcoinBasedTransaction> CreateSwapRedeemTxAsync(
            IBitcoinBasedTransaction paymentTx,
            long amount,
            string redeemAddress,
            byte[] redeemScript)
        {
            var currency = (BitcoinBasedCurrency)paymentTx.Currency;

            var swapOutput = paymentTx.Outputs
                .Cast<BitcoinBasedTxOutput>()
                .FirstOrDefault(o => o.IsPayToScriptHash(redeemScript));

            if (swapOutput == null)
                throw new Exception("Can't find pay to script hash output");

            var estimatedSigSize = BitcoinBasedCurrency.EstimateSigSize(swapOutput, forRedeem: true);

            var tx = currency.CreateP2PkhTx(
                unspentOutputs: new ITxOutput[] { swapOutput },
                destinationAddress: redeemAddress,
                changeAddress: redeemAddress,
                amount: amount,
                fee: 0,
                lockTime: DateTimeOffset.MinValue);

            // fee = txSize * feeRate without dust, because all coins will be send to one address
            var fee = (long) ((tx.VirtualSize() + estimatedSigSize) * currency.FeeRate);

            if (amount - fee < 0)
                throw new Exception($"Insufficient funds for fee. Available {amount}, required {fee}");

            tx = currency.CreateP2PkhTx(
                unspentOutputs: new ITxOutput[] { swapOutput },
                destinationAddress: redeemAddress,
                changeAddress: redeemAddress,
                amount: amount - fee,
                fee: fee,
                lockTime: DateTimeOffset.MinValue);

            return Task.FromResult(tx);
        }
    }
}