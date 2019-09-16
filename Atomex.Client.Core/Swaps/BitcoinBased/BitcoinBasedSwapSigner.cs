using System;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Core.Entities;
using Atomex.Wallet.Abstract;
using NBitcoin;
using Serilog;

namespace Atomex.Swaps.BitcoinBased
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
                .GetAvailableOutputsAsync(tx.Currency)
                .ConfigureAwait(false);

            var spentOutputs = outputs
                .Where(o => { return tx.Inputs.FirstOrDefault(i => i.Hash == o.TxId && i.Index == o.Index) != null; })
                .ToList();

            var result = await Account.Wallet
                .SignAsync(
                    tx: tx,
                    spentOutputs: spentOutputs,
                    addressResolver: Account)
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
            WalletAddress refundAddress)
        {
            var tx = refundTx.Clone();

            if (tx.Inputs.Length != 1)
            {
                Log.Error("Refund transaction has zero or more than one input");
                return null;
            }

            var spentOutput = paymentTx.Outputs.FirstOrDefault(o => o.IsSwapPayment);
            if (spentOutput == null)
            {
                Log.Error("Payment transaction hasn't swap output");
                return null;
            }

            // firstly check, if transaction is already signed
            if (tx.Verify(spentOutput))
                return tx;

            // clean any signature, if exists
            tx.NonStandardSign(Script.Empty, 0);

            var sigHash = tx.GetSignatureHash(spentOutput);

            var signature = await Account.Wallet
                .SignHashAsync(
                    hash: sigHash,
                    address: refundAddress)
                .ConfigureAwait(false);

            if (signature == null)
            {
                Log.Error("Refund transaction signature error");
                return null;
            }

            var refundScript = BitcoinBasedSwapTemplate.GenerateHtlcSwapRefund(
                aliceRefundSig: signature,
                aliceRefundPubKey: refundAddress.PublicKeyBytes());

            tx.NonStandardSign(refundScript, spentOutput);

            if (!tx.Verify(spentOutput, out var errors))
            {
                Log.Error("Refund transaction verify errors: {errors}", errors);
                return null;
            }

            return tx;
        }

        public async Task<IBitcoinBasedTransaction> SignHtlcSwapRefundForP2ShTxAsync(
            IBitcoinBasedTransaction refundTx,
            IBitcoinBasedTransaction paymentTx,
            WalletAddress refundAddress,
            byte[] redeemScript)
        {
            var tx = refundTx.Clone();

            if (tx.Inputs.Length != 1)
            {
                Log.Error("Refund transaction has zero or more than one input");
                return null;
            }

            var spentOutput = paymentTx.Outputs
                .Cast<BitcoinBasedTxOutput>()
                .FirstOrDefault(o => o.IsP2Sh);

            if (spentOutput == null)
            {
                Log.Error("Payment transaction hasn't swap output");
                return null;
            }

            // firstly check, if transaction is already signed
            if (tx.Verify(spentOutput))
                return tx;

            // clean any signature, if exists
            tx.NonStandardSign(Script.Empty, 0);

            var sigHash = tx.GetSignatureHash(new Script(redeemScript), spentOutput);

            var signature = await Account.Wallet
                .SignHashAsync(
                    hash: sigHash,
                    address: refundAddress)
                .ConfigureAwait(false);

            if (signature == null)
            {
                Log.Error("Refund transaction signature error");
                return null;
            }

            var refundScriptSig = BitcoinBasedSwapTemplate.GenerateHtlcSwapRefundForP2Sh(
                aliceRefundSig: signature,
                aliceRefundPubKey: refundAddress.PublicKeyBytes(),
                redeemScript: redeemScript);

            tx.NonStandardSign(refundScriptSig, spentOutput);

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
            WalletAddress redeemAddress,
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
                    address: redeemAddress)
                .ConfigureAwait(false);

            if (signature == null)
            {
                Log.Error("Redeem transaction signature error");
                return null;
            }

            var redeemScript = BitcoinBasedSwapTemplate.GenerateHtlcP2PkhSwapRedeem(
                sig: signature,
                pubKey: redeemAddress.PublicKeyBytes(),
                secret: secret);

            tx.NonStandardSign(redeemScript, spentOutput);

            if (!tx.Verify(spentOutput, out var errors))
            {
                Log.Error("Redeem transaction verify errors: {errors}", errors);
                return null;
            }

            return tx;
        }

        public async Task<IBitcoinBasedTransaction> SignHtlcRedeemTxForP2ShAsync(
            IBitcoinBasedTransaction redeemTx,
            IBitcoinBasedTransaction paymentTx,
            WalletAddress redeemAddress,
            byte[] secret,
            byte[] redeemScript)
        {
            var tx = redeemTx.Clone();

            var spentOutput = paymentTx.Outputs
                .Cast<BitcoinBasedTxOutput>()
                .FirstOrDefault(o => o.IsP2Sh);

            if (spentOutput == null)
            {
                Log.Error("Payment transaction hasn't swap output");
                return null;
            }

            var sigHash = tx.GetSignatureHash(new Script(redeemScript), spentOutput);

            var signature = await Account.Wallet
                .SignHashAsync(
                    hash: sigHash,
                    address: redeemAddress)
                .ConfigureAwait(false);

            if (signature == null)
            {
                Log.Error("Redeem transaction signature error");
                return null;
            }

            var redeemScriptSig = BitcoinBasedSwapTemplate.GenerateP2PkhSwapRedeemForP2Sh(
                sig: signature,
                pubKey: redeemAddress.PublicKeyBytes(),
                secret: secret,
                redeemScript: redeemScript);

            tx.NonStandardSign(redeemScriptSig, spentOutput);

            if (!tx.Verify(spentOutput, out var errors))
            {
                Log.Error("Redeem transaction verify errors: {errors}", errors);
                return null;
            }

            return tx;
        }
    }
}