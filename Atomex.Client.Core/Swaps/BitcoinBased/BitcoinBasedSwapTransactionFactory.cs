using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;

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

            availableOutputs.Sort((o1, o2) => o2.Value.CompareTo(o1.Value));

            var feeInSatoshi = 0L;
            ITxOutput[] selectedOutputs = null;

            for (var i = 1; i <= availableOutputs.Count; ++i)
            {
                selectedOutputs = availableOutputs
                    .Take(i)
                    .ToArray();

                var estimatedSigSize = BitcoinBasedCurrency.EstimateSigSize(selectedOutputs);

                var selectedInSatoshi = selectedOutputs.Sum(o => o.Value);

                if (selectedInSatoshi < amount) // insufficient funds
                    continue;

                var maxFeeInSatoshi = selectedInSatoshi - amount;

                var estimatedTx = currency
                    .CreateHtlcP2PkhScriptSwapPaymentTx(
                        unspentOutputs: selectedOutputs,
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

                var estimatedFeeInSatoshi = (long)(estimatedTxSize * currency.FeeRate);

                if (estimatedFeeInSatoshi > maxFeeInSatoshi) // insufficient funds
                    continue;

                var estimatedChangeInSatoshi = selectedInSatoshi - amount - estimatedFeeInSatoshi;

                // if estimated change is dust
                if (estimatedChangeInSatoshi >= 0 && estimatedChangeInSatoshi < currency.GetDust())
                {
                    feeInSatoshi = estimatedFeeInSatoshi + estimatedChangeInSatoshi;
                    break;
                }

                // if estimated change > dust
                var estimatedFeeWithChangeInSatoshi = (long)(estimatedTxSizeWithChange * currency.FeeRate);

                if (estimatedFeeWithChangeInSatoshi > maxFeeInSatoshi) // insufficient funds
                    continue;

                var esitmatedNewChangeInSatoshi = selectedInSatoshi - amount - estimatedFeeWithChangeInSatoshi;

                // if new estimated change is dust
                if (esitmatedNewChangeInSatoshi >= 0 && esitmatedNewChangeInSatoshi < currency.GetDust())
                {
                    feeInSatoshi = estimatedFeeWithChangeInSatoshi + esitmatedNewChangeInSatoshi;
                    break;
                }

                // if new estimated change > dust
                feeInSatoshi = estimatedFeeWithChangeInSatoshi;
                break;
            }

            if (selectedOutputs == null || feeInSatoshi == 0L)
                throw new Exception($"Insufficient funds. Available {availableOutputs.Sum(o => o.Value)}");

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

            if (sequenceNumber > 0)
                tx.SetSequenceNumber(sequenceNumber);

            return Task.FromResult(tx);
        }
    }
}