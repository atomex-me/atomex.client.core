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
                .GetAvailableOutputsAsync(fromWallets)
                .ConfigureAwait(false))
                .ToList();

            if (!availableOutputs.Any())
                throw new Exception($"Insufficient funds. Available 0.");

            var availableAmountInSatoshi = availableOutputs.Sum(o => o.Value);

            if (availableAmountInSatoshi <= amount)
                throw new Exception($"Insufficient funds. Available {availableOutputs.Sum(o => o.Value)}, required: {amount}");

            var feeInSatoshi = 0L;
            IEnumerable<ITxOutput> selectedOutputs = null;

            var feeRate = await currency
                .GetFeeRateAsync()
                .ConfigureAwait(false);

            foreach (var outputs in availableOutputs.SelectOutputs())
            {
                if (EstimateSelectedOutputs(
                    currency,
                    outputs,
                    amount,
                    feeRate,
                    refundAddress,
                    toAddress,
                    lockTime,
                    secretHash,
                    secretSize,
                    out feeInSatoshi))
                {
                    selectedOutputs = outputs;
                    break;
                }
            }

            if (selectedOutputs == null || feeInSatoshi == 0L)
            {
                // available outputs are not enough to send tx, let's try to change feeRate and try again
                var maxFeeInSatoshi = availableAmountInSatoshi - amount;

                var estimatedTx = currency
                     .CreateHtlcP2PkhScriptSwapPaymentTx(
                         unspentOutputs: availableOutputs,
                         aliceRefundAddress: refundAddress,
                         bobAddress: toAddress,
                         lockTime: lockTime,
                         secretHash: secretHash,
                         secretSize: secretSize,
                         amount: amount,
                         fee: maxFeeInSatoshi,
                         redeemScript: out var _);

                var estimatedSigSize       = BitcoinBasedCurrency.EstimateSigSize(availableOutputs);
                var estimatedTxVirtualSize = estimatedTx.VirtualSize();
                var estimatedTxSize        = estimatedTxVirtualSize + estimatedSigSize;
                var estimatedFeeRate       = maxFeeInSatoshi / estimatedTxSize;

                if (Math.Abs(feeRate - estimatedFeeRate) / feeRate > 0.1m)
                    throw new Exception($"Insufficient funds. Available {availableOutputs.Sum(o => o.Value)}, required: {amount}. Probably feeRate has changed a lot.");

                // sent tx with estimatedFeeRate without change
                selectedOutputs = availableOutputs;
                feeInSatoshi = maxFeeInSatoshi;
            }

            var tx = currency
                .CreateHtlcP2PkhScriptSwapPaymentTx(
                    unspentOutputs: selectedOutputs,
                    aliceRefundAddress: refundAddress,
                    bobAddress: toAddress,
                    lockTime: lockTime,
                    secretHash: secretHash,
                    secretSize: secretSize,
                    amount: amount,
                    fee: feeInSatoshi,
                    redeemScript: out var redeemScript);

            return (tx, redeemScript);
        }

        public static bool EstimateSelectedOutputs(
            BitcoinBasedCurrency currency,
            IEnumerable<ITxOutput> outputs,
            long amount,
            decimal feeRate,
            string refundAddress,
            string toAddress,
            DateTimeOffset lockTime,
            byte[] secretHash,
            int secretSize,
            out long feeInSatoshi)
        {
            feeInSatoshi = 0L;

            var selectedInSatoshi = outputs.Sum(o => o.Value);

            if (selectedInSatoshi < amount) // insufficient funds
                return false;

            var estimatedSigSize = BitcoinBasedCurrency.EstimateSigSize(outputs);

            var maxFeeInSatoshi = selectedInSatoshi - amount;

            var estimatedTx = currency
                .CreateHtlcP2PkhScriptSwapPaymentTx(
                    unspentOutputs: outputs,
                    aliceRefundAddress: refundAddress,
                    bobAddress: toAddress,
                    lockTime: lockTime,
                    secretHash: secretHash,
                    secretSize: secretSize,
                    amount: amount,
                    fee: maxFeeInSatoshi,
                    redeemScript: out _);

            var estimatedTxVirtualSize = estimatedTx.VirtualSize();
            var estimatedTxSize = estimatedTxVirtualSize + estimatedSigSize;
            var estimatedTxSizeWithChange = estimatedTxVirtualSize + estimatedSigSize + BitcoinBasedCurrency.OutputSize;

            var estimatedFeeInSatoshi = (long)(estimatedTxSize * feeRate);

            if (estimatedFeeInSatoshi > maxFeeInSatoshi) // insufficient funds
                return false;

            var estimatedChangeInSatoshi = selectedInSatoshi - amount - estimatedFeeInSatoshi;

            // if estimated change is dust
            if (estimatedChangeInSatoshi >= 0 && estimatedChangeInSatoshi < currency.GetDust())
            {
                feeInSatoshi = estimatedFeeInSatoshi + estimatedChangeInSatoshi;
                return true;
            }

            // if estimated change > dust
            var estimatedFeeWithChangeInSatoshi = (long)(estimatedTxSizeWithChange * feeRate);

            if (estimatedFeeWithChangeInSatoshi > maxFeeInSatoshi) // insufficient funds
                return false;

            var esitmatedNewChangeInSatoshi = selectedInSatoshi - amount - estimatedFeeWithChangeInSatoshi;

            // if new estimated change is dust
            if (esitmatedNewChangeInSatoshi >= 0 && esitmatedNewChangeInSatoshi < currency.GetDust())
            {
                feeInSatoshi = estimatedFeeWithChangeInSatoshi + esitmatedNewChangeInSatoshi;
                return true;
            }

            // if new estimated change > dust
            feeInSatoshi = estimatedFeeWithChangeInSatoshi;

            return true;
        }

        public async Task<IBitcoinBasedTransaction> CreateSwapRefundTxAsync(
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

            var feeRate = await currency
                .GetFeeRateAsync()
                .ConfigureAwait(false);

            // fee = txSize * feeRate without dust, because all coins will be send to one address
            var fee = (long) ((tx.VirtualSize() + estimatedSigSize) * feeRate);

            if (amount - fee < 0)
                throw new Exception($"Insufficient funds for fee. Available {amount}, required {fee}");

            tx = currency.CreateP2PkhTx(
                unspentOutputs: new ITxOutput[] { swapOutput },
                destinationAddress: refundAddress,
                changeAddress: refundAddress,
                amount: amount - fee,
                fee: fee,
                lockTime: lockTime);

            return tx;
        }

        public async Task<IBitcoinBasedTransaction> CreateSwapRedeemTxAsync(
            IBitcoinBasedTransaction paymentTx,
            long amount,
            string redeemAddress,
            byte[] redeemScript,
            uint sequenceNumber = 0)
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

            var feeRate = await currency
                .GetFeeRateAsync()
                .ConfigureAwait(false);

            // fee = txSize * feeRate without dust, because all coins will be send to one address
            var fee = (long) ((tx.VirtualSize() + estimatedSigSize) * feeRate);

            if (amount - fee < 0)
                throw new Exception($"Insufficient funds for fee. Available {amount}, required {fee}");

            tx = currency.CreateP2PkhTx(
                unspentOutputs: new ITxOutput[] { swapOutput },
                destinationAddress: redeemAddress,
                changeAddress: redeemAddress,
                amount: amount - fee,
                fee: fee,
                lockTime: DateTimeOffset.MinValue);

            if (sequenceNumber > 0)
                tx.SetSequenceNumber(sequenceNumber);

            return tx;
        }
    }
}