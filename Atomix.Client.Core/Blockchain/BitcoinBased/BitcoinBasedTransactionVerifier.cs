using System;
using System.Linq;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Entities;
using NBitcoin;
using Serilog;

namespace Atomix.Blockchain.BitcoinBased
{
    public static class BitcoinBasedTransactionVerifier
    {
        public static bool TryVerifyPartyPaymentTx(
            IBitcoinBasedTransaction tx,
            ClientSwap swap,
            byte[] secretHash,
            long refundLockTime,
            out Error error)
        {
            if (tx == null)
                throw new ArgumentNullException(nameof(tx));

            if (swap == null)
                throw new ArgumentNullException(nameof(swap));

            // check swap output
            if (!tx.SwapOutputs().Any()) {
                error = new Error(
                    code: Errors.InvalidSwapPaymentTx,
                    description: $"No swap outputs in tx @{tx.Id}",
                    swap: swap);
                return false;
            }

            var targetAddressHash = new BitcoinPubKeyAddress(swap.ToAddress)
                .Hash
                .ToBytes();

            var hasSwapOutput = false;

            foreach (var txOutput in tx.SwapOutputs())
            {
                try
                {
                    var output = (BitcoinBasedTxOutput)txOutput;

                    // check address
                    var outputTargetAddressHash = BitcoinBasedSwapTemplate.ExtractTargetPkhFromHtlcP2PkhSwapPayment(
                        script: output.Coin.TxOut.ScriptPubKey);

                    if (!outputTargetAddressHash.SequenceEqual(targetAddressHash))
                        continue;

                    var outputSecretHash = BitcoinBasedSwapTemplate.ExtractSecretHashFromHtlcP2PkhSwapPayment(
                        script: output.Coin.TxOut.ScriptPubKey);

                    if (!outputSecretHash.SequenceEqual(secretHash))
                        continue;

                    hasSwapOutput = true;

                    // check swap output refund lock time
                    var outputLockTime = BitcoinBasedSwapTemplate.ExtractLockTimeFromHtlcP2PkhSwapPayment(
                        script: output.Coin.TxOut.ScriptPubKey);

                    var swapTimeUnix = (long)swap.TimeStamp.ToUniversalTime().ToUnixTime();

                    if (outputLockTime - swapTimeUnix < refundLockTime)
                    {
                        error = new Error(
                            code: Errors.InvalidRefundLockTime,
                            description: "Invalid refund time",
                            swap: swap);

                        return false;
                    }

                    // check swap amount
                    var currency = (BitcoinBasedCurrency) swap.PurchasedCurrency;

                    var side = swap.Symbol
                        .OrderSideForBuyCurrency(currency)
                        .Opposite();

                    var requiredAmount = AmountHelper.QtyToAmount(side, swap.Qty, swap.Price);
                    var requiredAmountInSatoshi = currency.CoinToSatoshi(requiredAmount);

                    if (output.Value < requiredAmountInSatoshi)
                    {
                        error = new Error(
                            code: Errors.InvalidSwapPaymentTxAmount,
                            description: "Invalid payment tx amount",
                            swap: swap);

                        return false;
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "Transaction verification error");

                    error = new Error(
                        code: Errors.TransactionVerificationError,
                        description: e.Message,
                        swap: swap);

                    return false;
                }
            }

            if (!hasSwapOutput)
            {
                error = new Error(
                    code: Errors.TransactionVerificationError,
                    description: $"No swap outputs in tx @{tx.Id} for address {swap.ToAddress}",
                    swap: swap);

                return false;
            }

            // todo: check fee
            // todo: try to verify

            error = null;
            return true;
        }
    }
}