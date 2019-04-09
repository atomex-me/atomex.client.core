using Atomix.Blockchain.BitcoinBased;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using NBitcoin;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Atomix.Swaps.BitcoinBased
{
    public class BitcoinBasedSwapSigner
    {
        private IAccount Account { get; }

        public BitcoinBasedSwapSigner(IAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task<IBitcoinBasedTransaction> SignPaymentTxAsync(
            IBitcoinBasedTransaction paymentTx)
        {
            var tx = paymentTx.Clone();

            var outputs = await Account
                .GetUnspentOutputsAsync(tx.Currency)
                .ConfigureAwait(false);

            var spentOutputs = outputs
                .Where(o => { return tx.Inputs.FirstOrDefault(i => i.Hash == o.TxId && i.Index == o.Index) != null; })
                .ToList();

            var result = await Account.Wallet
                .SignAsync(tx, spentOutputs)
                .ConfigureAwait(false);

            if (!result)
            {
                Log.Error("Payment transaction signature error");
                return null;
            }

            if (!tx.Verify(spentOutputs, out var errors))
            {
                Log.Error("Payment transaction verify errors: {errors}", errors);
                return null;
            }

            return tx;
        }

        public async Task<IBitcoinBasedTransaction> SignRefundTxAsync(
            IBitcoinBasedTransaction refundTx,
            IBitcoinBasedTransaction paymentTx,
            Order order)
        {
            var tx = refundTx.Clone();

            if (tx.Inputs.Length != 1)
            {
                Log.Error("Refund transaction has zero or more than one input");
                return null;
            }

            if (!(paymentTx.Outputs.FirstOrDefault(o => o.IsSwapPayment) is BitcoinBasedTxOutput spentOutput))
            {
                Log.Error("Payment transaction hasn't swap output");
                return null;
            }

            spentOutput = spentOutput.WithPatchedTxId(tx.Inputs.First().Hash);

            var sigHash = tx.GetSignatureHash(spentOutput);

            var signature = await Account.Wallet
                .SignHashAsync(
                    hash: sigHash,
                    address: order.ToWallet)
                .ConfigureAwait(false);

            if (signature == null)
            {
                Log.Error("Refund transaction signature error");
                return null;
            }

            var signScript = BitcoinBasedSwapTemplate.GenerateSwapRefundByBob(signature);

            tx.NonStandardSign(signScript, spentOutput);

            return tx;
        }

        public async Task<IBitcoinBasedTransaction> SignSelfRefundTxAsync(
            IBitcoinBasedTransaction refundTx,
            IBitcoinBasedTransaction paymentTx,
            Order order)
        {
            var tx = refundTx.Clone();

            if (tx.Inputs.Length != 1)
            {
                Log.Error("Refund transaction has zero or more than one input");
                return null;
            }

            // get counter party signature from refund tx
            var counterPartyScriptSig = tx.GetScriptSig(0);
            var counterPartySign = BitcoinBasedSwapTemplate.ExtractSignFromP2PkhSwapRefund(counterPartyScriptSig);

            // clean counter party signature in refund tx
            tx.NonStandardSign(Script.Empty, 0);

            var spentOutput = paymentTx.Outputs.FirstOrDefault(o => o.IsSwapPayment);
            if (spentOutput == null)
            {
                Log.Error("Payment transaction hasn't swap output");
                return null;
            }

            var sigHash = tx.GetSignatureHash(spentOutput);

            var signature = await Account.Wallet
                .SignHashAsync(
                    hash: sigHash,
                    address: order.RefundWallet)
                .ConfigureAwait(false);

            if (signature == null)
            {
                Log.Error("Refund transaction signature error");
                return null;
            }

            var refundScript = BitcoinBasedSwapTemplate.GenerateSwapRefund(
                aliceRefundSig: signature,
                bobRefundSig: counterPartySign);

            tx.NonStandardSign(refundScript, spentOutput);

            if (!tx.Verify(spentOutput, out var errors))
            {
                Log.Error("Refund transaction verify errors: {errors}", errors);
                return null;
            }

            return tx;
        }

        public async Task<IBitcoinBasedTransaction> SignRedeemTxAsync(
            IBitcoinBasedTransaction redeemTx,
            IBitcoinBasedTransaction paymentTx,
            Order order,
            byte[] secret)
        {
            var tx = redeemTx.Clone();

            var spentOutput = paymentTx.Outputs.FirstOrDefault(o => o.IsSwapPayment);
            if (spentOutput == null)
            {
                Log.Error("Payment transaction hasn't swap output");
                return null;
            }

            var sigHash = tx.GetSignatureHash(spentOutput);

            var signature = await Account.Wallet
                .SignHashAsync(
                    hash: sigHash,
                    address: order.ToWallet)
                .ConfigureAwait(false);

            if (signature == null)
            {
                Log.Error("Redeem transaction signature error");
                return null;
            }

            var redeemScript = BitcoinBasedSwapTemplate.GenerateP2PkhSwapRedeem(
                sig: signature,
                pubKey: order.ToWallet.PublicKeyBytes(),
                secret: secret);

            tx.NonStandardSign(redeemScript, spentOutput);

            if (!tx.Verify(spentOutput, out var errors))
            {
                Log.Error("Redeem transaction verify errors: {errors}", errors);
                return null;
            }

            return tx;
        }
    }
}