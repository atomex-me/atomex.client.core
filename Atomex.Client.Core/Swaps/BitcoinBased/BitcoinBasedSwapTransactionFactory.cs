using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;

namespace Atomex.Swaps.BitcoinBased
{
    public class BitcoinBasedSwapTransactionFactory : IBitcoinBasedSwapTransactionFactory
    {
        //public const decimal MaxFeeRateChangePercent = 0.5m;
        public const int MinFeeRate = 1;

        public async Task<BitcoinBasedTransaction> CreateSwapPaymentTxAsync(
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
                throw new Exception($"Insufficient funds. Available {availableAmountInSatoshi}, required: {amount}");

            var feeRate = await currencyConfig
                .GetFeeRateAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var lockScript = BitcoinBasedSwapTemplate.GenerateHtlcP2PkhSwapPayment(
                aliceRefundAddress: refundAddress,
                bobAddress: toAddress,
                lockTimeStamp: lockTime.ToUnixTimeSeconds(),
                secretHash: secretHash,
                secretSize: secretSize,
                expectedNetwork: currencyConfig.Network);

            var feeInSatoshi = 0L;

            var inputsToSign = fromOutputs
                .Select(o => new BitcoinInputToSign { Output = o });

            var destinations = new BitcoinDestination[]
            {
                new BitcoinDestination
                {
                    Script = lockScript.PaymentScript,
                    AmountInSatoshi = amount
                }
            };

            var txParams = await BitcoinTransactionParams.SelectTransactionParamsByFeeRateAsync(
                    availableInputs: inputsToSign,
                    destinations: destinations,
                    changeAddress: refundAddress,
                    feeRate: feeRate,
                    currencyConfig: currencyConfig,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txParams == null) // can't create tx with required feeRate => let's try to change feeRate and try again
            {
                var maxFeeInSatoshi = availableAmountInSatoshi - amount;

                var (txSize, txSizeWithChange) = BitcoinTransactionParams.CalculateTxSize(
                    inputsCount: fromOutputs.Count(),
                    inputsSize: inputsToSign.Sum(i => i.SizeWithSignature()),
                    outputsCount: destinations.Length,
                    outputsSize: destinations.Sum(d => d.Size()),
                    witnessCount: fromOutputs.Sum(o => o.IsSegWit ? 1 : 0),
                    changeOutputSize: BitcoinTransactionParams.CalculateChangeOutputSize(
                        changeAddress: refundAddress,
                        network: currencyConfig.Network));

                var estimatedFeeRate = maxFeeInSatoshi / txSizeWithChange;

                if (estimatedFeeRate > feeRate)
                    Log.Error($"EstimatedFeeRate changed: {estimatedFeeRate}, old fee rate: {feeRate}");

                if (estimatedFeeRate < MinFeeRate)
                    throw new Exception($"Insufficient funds, estimated fee rate less than {MinFeeRate} satoshi/byte");

                //if (Math.Abs(feeRate - estimatedFeeRate) / feeRate > MaxFeeRateChangePercent)
                //    throw new Exception($"Insufficient funds. Available {availableAmountInSatoshi}, required: {amount}. Probably feeRate has changed a lot.");

                feeInSatoshi = maxFeeInSatoshi;
            }
            else
            {
                feeInSatoshi = (long)txParams.FeeInSatoshi;
            }

            var tx = currencyConfig
                .CreateHtlcP2PkhScriptSwapPaymentTx(
                    unspentOutputs: txParams?.InputsToSign.Select(i => i.Output) ?? fromOutputs,
                    aliceRefundAddress: refundAddress,
                    bobAddress: toAddress,
                    lockTime: lockTime,
                    secretHash: secretHash,
                    secretSize: secretSize,
                    amount: amount,
                    fee: feeInSatoshi,
                    redeemScript: out _);

            return tx;
        }

        public async Task<BitcoinBasedTransaction> CreateSwapRefundTxAsync(
            BitcoinBasedTransaction paymentTx,
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

            var inputSize = new BitcoinInputToSign
            {
                Output = swapOutput,
                Signer = new BitcoinRefundSigner()

            }.SizeWithSignature();

            var outputSize = new BitcoinDestination
            {
                AmountInSatoshi = amount,
                Script = BitcoinAddress.Create(refundAddress, currencyConfig.Network).ScriptPubKey

            }.Size();

            var (txSize, _) = BitcoinTransactionParams.CalculateTxSize(
                    inputsCount: 1,
                    inputsSize: inputSize,
                    outputsCount: 1,
                    outputsSize: outputSize,
                    witnessCount: swapOutput.IsSegWit ? 1 : 0,
                    changeOutputSize: 0);

            var feeInSatoshi = (long)(txSize * feeRate);

            if (amount - feeInSatoshi < 0)
                throw new Exception($"Insufficient funds for fee. Available {amount}, required {feeInSatoshi}");

            return currencyConfig.CreateP2PkhTx(
                unspentOutputs: new BitcoinBasedTxOutput[] { swapOutput },
                destinationAddress: refundAddress,
                changeAddress: refundAddress,
                amount: amount - feeInSatoshi,
                fee: feeInSatoshi,
                lockTime: lockTime,
                knownRedeems: new Script(redeemScript));
        }

        public async Task<BitcoinBasedTransaction> CreateSwapRedeemTxAsync(
            BitcoinBasedTransaction paymentTx,
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

            var inputSize = new BitcoinInputToSign
            {
                Output = swapOutput,
                Signer = new BitcoinRedeemSigner(secret: null)

            }.SizeWithSignature();

            var outputSize = new BitcoinDestination
            {
                AmountInSatoshi = amount,
                Script = BitcoinAddress.Create(redeemAddress, currencyConfig.Network).ScriptPubKey

            }.Size();

            var (txSize, _) = BitcoinTransactionParams.CalculateTxSize(
                    inputsCount: 1,
                    inputsSize: inputSize,
                    outputsCount: 1,
                    outputsSize: outputSize,
                    witnessCount: swapOutput.IsSegWit ? 1 : 0,
                    changeOutputSize: 0);

            var feeInSatoshi = (long)(txSize * feeRate);

            if (amount - feeInSatoshi < 0)
                throw new Exception($"Insufficient funds for fee. Available {amount}, required {feeInSatoshi}");

            var tx = currencyConfig.CreateP2PkhTx(
                unspentOutputs: new BitcoinBasedTxOutput[] { swapOutput },
                destinationAddress: redeemAddress,
                changeAddress: redeemAddress,
                amount: amount - feeInSatoshi,
                fee: feeInSatoshi,
                lockTime: DateTimeOffset.MinValue,
                knownRedeems: new Script(redeemScript));

            if (sequenceNumber > 0)
                tx.SetSequenceNumber(sequenceNumber);

            return tx;
        }
    }
}