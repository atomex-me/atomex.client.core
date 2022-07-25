using System;
using System.Linq;
using System.Threading.Tasks;

using NBitcoin;
using Serilog;

using Atomex.Blockchain.BitcoinBased;
using Atomex.Core;
using Atomex.Wallet.BitcoinBased;

namespace Atomex.Swaps.BitcoinBased
{
    public class BitcoinBasedSwapSigner
    {
        private BitcoinBasedAccount Account { get; }

        public BitcoinBasedSwapSigner(BitcoinBasedAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task<BitcoinBasedTransaction> SignPaymentTxAsync(
            BitcoinBasedTransaction paymentTx)
        {
            var tx = paymentTx.Clone();

            var outputs = await Account
                .GetAvailableOutputsAsync()
                .ConfigureAwait(false);

            var spentOutputs = outputs
                .Where(o => { return tx.Inputs.FirstOrDefault(i => i.Hash == o.TxId && i.Index == o.Index) != null; })
                .ToList();

            var result = await Account.Wallet
                .SignAsync(
                    tx: tx,
                    spentOutputs: spentOutputs,
                    addressResolver: Account,
                    currencyConfig: Account.Config)
                .ConfigureAwait(false);

            if (!result)
            {
                Log.Error("Payment transaction signature error");
                return null;
            }

            if (!tx.Verify(spentOutputs, out var errors, Account.Config))
            {
                Log.Error("Payment transaction verify errors: {errors}", errors);
                return null;
            }

            return tx;
        }

        public Task<BitcoinBasedTransaction> SignRefundTxAsync(
            BitcoinBasedTransaction refundTx,
            BitcoinBasedTransaction paymentTx,
            WalletAddress refundAddress,
            byte[] redeemScript)
        {
            return SignHtlcSwapRefundForP2ShTxAsync(refundTx, paymentTx, refundAddress, redeemScript);
        }

        public async Task<BitcoinBasedTransaction> SignHtlcSwapRefundForP2ShTxAsync(
            BitcoinBasedTransaction refundTx,
            BitcoinBasedTransaction paymentTx,
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
                .FirstOrDefault(o => o.IsPayToScriptHash(redeemScript));

            if (spentOutput == null)
            {
                Log.Error("Payment transaction hasn't pay to script hash output for redeem script");
                return null;
            }

            // firstly check, if transaction is already signed
            if (tx.Verify(spentOutput, Account.Config))
                return tx;

            // clean any signature, if exists
            tx.NonStandardSign(Script.Empty, 0);

            var sigHash = tx.GetSignatureHash(spentOutput, new Script(redeemScript));

            var signature = await Account.Wallet
                .SignHashAsync(
                    hash: sigHash,
                    address: refundAddress,
                    currency: Account.Currencies.GetByName(Account.Currency))
                .ConfigureAwait(false);

            if (signature == null)
            {
                Log.Error("Refund transaction signature error");
                return null;
            }

            using var refundAddressPublicKey = Account.Wallet.GetPublicKey(
                currency: Account.Config,
                keyIndex: refundAddress.KeyIndex,
                keyType: refundAddress.KeyType);

            var refundScriptSig = BitcoinBasedSwapTemplate.GenerateHtlcSwapRefundForP2Sh(
                aliceRefundSig: signature,
                aliceRefundPubKey: refundAddressPublicKey.ToUnsecuredBytes(),
                redeemScript: redeemScript);

            tx.NonStandardSign(refundScriptSig, spentOutput);

            if (!tx.Verify(spentOutput, out var errors, Account.Config))
            {
                Log.Error("Refund transaction verify errors: {errors}", errors);
                return null;
            }

            return tx;
        }

        public Task<BitcoinBasedTransaction> SignRedeemTxAsync(
            BitcoinBasedTransaction redeemTx,
            BitcoinBasedTransaction paymentTx,
            WalletAddress redeemAddress,
            byte[] secret,
            byte[] redeemScript)
        {
            return SignHtlcSwapRedeemForP2ShTxAsync(redeemTx, paymentTx, redeemAddress, secret, redeemScript);
        }

        public async Task<BitcoinBasedTransaction> SignHtlcSwapRedeemForP2ShTxAsync(
            BitcoinBasedTransaction redeemTx,
            BitcoinBasedTransaction paymentTx,
            WalletAddress redeemAddress,
            byte[] secret,
            byte[] redeemScript)
        {
            var tx = redeemTx.Clone();

            var spentOutput = paymentTx.Outputs
                .Cast<BitcoinBasedTxOutput>()
                .FirstOrDefault(o => o.IsPayToScriptHash(redeemScript));

            if (spentOutput == null)
            {
                Log.Error("Payment transaction hasn't pay to script hash output for redeem script");
                return null;
            }

            var sigHash = tx.GetSignatureHash(spentOutput, new Script(redeemScript));

            var signature = await Account.Wallet
                .SignHashAsync(
                    hash: sigHash,
                    address: redeemAddress,
                    currency: Account.Currencies.GetByName(Account.Currency))
                .ConfigureAwait(false);

            if (signature == null)
            {
                Log.Error("Redeem transaction signature error");
                return null;
            }

            using var redeemAddressPublicKey = Account.Wallet.GetPublicKey(
                currency: Account.Config,
                keyIndex: redeemAddress.KeyIndex,
                keyType: redeemAddress.KeyType);

            var redeemScriptSig = BitcoinBasedSwapTemplate.GenerateP2PkhSwapRedeemForP2Sh(
                sig: signature,
                pubKey: redeemAddressPublicKey.ToUnsecuredBytes(),
                secret: secret,
                redeemScript: redeemScript);

            tx.NonStandardSign(redeemScriptSig, spentOutput);

            if (!tx.Verify(spentOutput, out var errors, Account.Config))
            {
                Log.Error("Redeem transaction verify errors: {errors}", errors);
                return null;
            }

            return tx;
        }
    }
}