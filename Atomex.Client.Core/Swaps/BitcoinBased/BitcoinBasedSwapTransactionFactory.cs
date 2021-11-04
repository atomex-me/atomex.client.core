using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Serilog;

namespace Atomex.Swaps.BitcoinBased
{
    public class BitcoinBasedSwapTransactionFactory : IBitcoinBasedSwapTransactionFactory
    {
        public async Task<IBitcoinBasedTransaction> CreateSwapPaymentTxAsync(
            IEnumerable<BitcoinBasedTxOutput> fromOutputs,
            long amount,
            string refundAddress,
            string toAddress,
            DateTimeOffset lockTime,
            byte[] secretHash,
            int secretSize,
            BitcoinBasedConfig currencyConfig,
            CancellationToken cancellationToken = default)
        {
            var availableAmountInSatoshi = fromOutputs.Sum(o => o.Value);

            if (availableAmountInSatoshi <= amount)
                throw new Exception($"Insufficient funds. Available {fromOutputs.Sum(o => o.Value)}, required: {amount}");

            var feeRate = await currencyConfig
                .GetFeeRateAsync()
                .ConfigureAwait(false);

            var lockScript = BitcoinBasedSwapTemplate.GenerateHtlcP2PkhSwapPayment(
                aliceRefundAddress: refundAddress,
                bobAddress: toAddress,
                lockTimeStamp: lockTime.ToUnixTimeSeconds(),
                secretHash: secretHash,
                secretSize: secretSize,
                expectedNetwork: currencyConfig.Network);

            //var feeInSatoshi = 0L;

            var txParams = await BitcoinTransactionParams.SelectTransactionParamsAsync(
                    availableInputs: fromOutputs.Select(o => new BitcoinInputToSign
                    {
                        Output = o
                    }),
                    destinations: new BitcoinDestination[]
                    {
                        new BitcoinDestination
                        {
                            Script = lockScript.PaymentScript,
                            AmountInSatoshi = amount
                        }
                    },
                    changeAddress: refundAddress,
                    feeRate: feeRate,
                    currencyConfig: currencyConfig,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txParams == null)
            {
                //Log.Error
            }

            //var tx = currencyConfig
            //    .CreateHtlcP2PkhScriptSwapPaymentTx(
            //        unspentOutputs: txParams.InputsToSign.Select(i => i.Output),
            //        aliceRefundAddress: refundAddress,
            //        bobAddress: toAddress,
            //        lockTime: lockTime,
            //        secretHash: secretHash,
            //        secretSize: secretSize,
            //        amount: amount,
            //        fee: feeInSatoshi,
            //        redeemScript: out _);

            //return tx;



            //if (selectedOutputs == null || feeInSatoshi == 0L)
            //{
            //    // available outputs are not enough to send tx, let's try to change feeRate and try again
            //    var maxFeeInSatoshi = availableAmountInSatoshi - amount;

            //    var estimatedTx = currency
            //         .CreateHtlcP2PkhScriptSwapPaymentTx(
            //             unspentOutputs: availableOutputs,
            //             aliceRefundAddress: refundAddress,
            //             bobAddress: toAddress,
            //             lockTime: lockTime,
            //             secretHash: secretHash,
            //             secretSize: secretSize,
            //             amount: amount,
            //             fee: maxFeeInSatoshi,
            //             redeemScript: out _);

            //    var estimatedSigSize       = BitcoinBasedConfig.EstimateSigSize(availableOutputs);
            //    var estimatedTxVirtualSize = estimatedTx.VirtualSize();
            //    var estimatedTxSize        = estimatedTxVirtualSize + estimatedSigSize;
            //    var estimatedFeeRate       = maxFeeInSatoshi / estimatedTxSize;

            //    if (Math.Abs(feeRate - estimatedFeeRate) / feeRate > 0.1m)
            //        throw new Exception($"Insufficient funds. Available {availableOutputs.Sum(o => o.Value)}, required: {amount}. Probably feeRate has changed a lot.");

            //    // sent tx with estimatedFeeRate without change
            //    selectedOutputs = availableOutputs;
            //    feeInSatoshi = maxFeeInSatoshi;
            //}
        }

        public async Task<IBitcoinBasedTransaction> CreateSwapRefundTxAsync(
            IBitcoinBasedTransaction paymentTx,
            long amount,
            string refundAddress,
            byte[] redeemScript,
            DateTimeOffset lockTime,
            BitcoinBasedConfig currencyConfig)
        {
            var swapOutput = paymentTx.Outputs
                .Cast<BitcoinBasedTxOutput>()
                .FirstOrDefault(o => o.IsPayToScriptHash(redeemScript));

            if (swapOutput == null)
                throw new Exception("Can't find pay to script hash output");

            var feeRate = await currencyConfig
                .GetFeeRateAsync()
                .ConfigureAwait(false);

            //var txParams = await BitcoinTransactionParams.SelectTransactionParamsAsync(
            //    availableInputs: new BitcoinInputToSign[] {
            //        new BitcoinInputToSign
            //        {
            //            Output = swapOutput,
            //            //Signer = new 
            //        }
            //    },
            //    destinations: new BitcoinDestination[]
            //    {
            //        new BitcoinDestination
            //        {
            //            Script = BitcoinAddress.Create(refundAddress, currencyConfig.Network).ScriptPubKey,
            //            AmountInSatoshi = amount
            //        }
            //    },
            //    changeAddress: refundAddress,
            //    feeRate: feeRate,
            //    currencyConfig: currencyConfig,
            //    cancellationToken: cancellationToken)
            //.ConfigureAwait(false);

            //var estimatedSigSize = BitcoinBasedConfig.EstimateSigSize(swapOutput, forRefund: true);

            //var tx = currencyConfig.CreateP2PkhTx(
            //    unspentOutputs: new ITxOutput[] { swapOutput },
            //    destinationAddress: refundAddress,
            //    changeAddress: refundAddress,
            //    amount: amount,
            //    fee: 0,
            //    lockTime: lockTime,
            //    knownRedeems: new Script(redeemScript));

            //var feeRate = await currencyConfig
            //    .GetFeeRateAsync()
            //    .ConfigureAwait(false);

            //// fee = txSize * feeRate without dust, because all coins will be send to one address
            //var fee = (long) ((tx.VirtualSize() + estimatedSigSize) * feeRate);

            //if (amount - fee < 0)
            //    throw new Exception($"Insufficient funds for fee. Available {amount}, required {fee}");

            //tx = currencyConfig.CreateP2PkhTx(
            //    unspentOutputs: new ITxOutput[] { swapOutput },
            //    destinationAddress: refundAddress,
            //    changeAddress: refundAddress,
            //    amount: amount - fee,
            //    fee: fee,
            //    lockTime: lockTime,
            //    knownRedeems: new Script(redeemScript));

            //return tx;
        }

        public async Task<IBitcoinBasedTransaction> CreateSwapRedeemTxAsync(
            IBitcoinBasedTransaction paymentTx,
            long amount,
            string redeemAddress,
            byte[] redeemScript,
            BitcoinBasedConfig currencyConfig,
            uint sequenceNumber = 0)
        {
            var swapOutput = paymentTx.Outputs
                .Cast<BitcoinBasedTxOutput>()
                .FirstOrDefault(o => o.IsPayToScriptHash(redeemScript));

            if (swapOutput == null)
                throw new Exception("Can't find pay to script hash output");

            var feeRate = await currencyConfig
                .GetFeeRateAsync()
                .ConfigureAwait(false);

            //var estimatedSigSize = BitcoinBasedConfig.EstimateSigSize(swapOutput, forRedeem: true);

            //var tx = currencyConfig.CreateP2PkhTx(
            //    unspentOutputs: new ITxOutput[] { swapOutput },
            //    destinationAddress: redeemAddress,
            //    changeAddress: redeemAddress,
            //    amount: amount,
            //    fee: 0,
            //    lockTime: DateTimeOffset.MinValue,
            //    knownRedeems: new Script(redeemScript));

            //var feeRate = await currencyConfig
            //    .GetFeeRateAsync()
            //    .ConfigureAwait(false);

            //// fee = txSize * feeRate without dust, because all coins will be send to one address
            //var fee = (long) ((tx.VirtualSize() + estimatedSigSize) * feeRate);

            //if (amount - fee < 0)
            //    throw new Exception($"Insufficient funds for fee. Available {amount}, required {fee}");

            //tx = currencyConfig.CreateP2PkhTx(
            //    unspentOutputs: new ITxOutput[] { swapOutput },
            //    destinationAddress: redeemAddress,
            //    changeAddress: redeemAddress,
            //    amount: amount - fee,
            //    fee: fee,
            //    lockTime: DateTimeOffset.MinValue,
            //    knownRedeems: new Script(redeemScript));

            //if (sequenceNumber > 0)
            //    tx.SetSequenceNumber(sequenceNumber);

            //return tx;
        }
    }
}