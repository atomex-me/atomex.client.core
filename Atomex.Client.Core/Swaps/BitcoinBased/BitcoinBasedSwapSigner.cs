using System;
using System.Linq;
using System.Threading.Tasks;

using NBitcoin;
using Serilog;

using Atomex.Blockchain.Bitcoin;
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

        public async Task<BitcoinTransaction> SignPaymentTxAsync(BitcoinTransaction tx)
        {
            var outputs = await Account
                .GetAvailableOutputsAsync()
                .ConfigureAwait(false);

            var spentOutputs = outputs
                .Where(o => { return tx.Inputs.FirstOrDefault(i => i.PreviousOutput.Hash == o.TxId && i.Index == o.Index) != null; })
                .ToList();

            var result = await Account
                .SignAsync(
                    tx: tx,
                    spentOutputs: spentOutputs)
                .ConfigureAwait(false);

            if (!result)
            {
                Log.Error("Payment transaction signature error");
                return null;
            }

            if (!tx.Verify(spentOutputs, out var errors, Account.Config.Network))
            {
                Log.Error("Payment transaction verify errors: {errors}", errors);
                return null;
            }

            return tx;
        }

        public Task<BitcoinTransaction> SignRefundTxAsync(
            BitcoinTransaction refundTx,
            BitcoinTransaction paymentTx,
            WalletAddress refundAddress,
            byte[] redeemScript)
        {
            return SignHtlcSwapRefundForP2ShTxAsync(refundTx, paymentTx, refundAddress, redeemScript);
        }

        public async Task<BitcoinTransaction> SignHtlcSwapRefundForP2ShTxAsync(
            BitcoinTransaction tx,
            BitcoinTransaction paymentTx,
            WalletAddress refundAddress,
            byte[] redeemScript)
        {
            if (tx.Inputs.Length != 1)
            {
                Log.Error("Refund transaction has zero or more than one input");
                return null;
            }

            var spentOutput = paymentTx.Outputs
                .Cast<BitcoinTxOutput>()
                .FirstOrDefault(o => o.IsPayToScriptHash(redeemScript));

            if (spentOutput == null)
            {
                Log.Error("Payment transaction hasn't pay to script hash output for redeem script");
                return null;
            }

            // firstly check, if transaction is already signed
            if (tx.Verify(spentOutput, Account.Config.Network))
                return tx;

            // clean any signature, if exists
            tx.ClearSignatures(0);

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
                keyPath: refundAddress.KeyPath,
                keyType: refundAddress.KeyType);

            var refundScriptSig = BitcoinSwapTemplate.CreateSwapRefundScript(
                aliceRefundSig: signature,
                aliceRefundPubKey: refundAddressPublicKey.ToUnsecuredBytes(),
                redeemScript: redeemScript);

            var refundScriptSigSegwit = refundScriptSig.ToWitScript();

            tx.SetSignature(refundScriptSigSegwit, spentOutput);

            if (!tx.Verify(spentOutput, out var errors, Account.Config.Network))
            {
                Log.Error("Refund transaction verify errors: {errors}", errors);
                return null;
            }

            return tx;
        }

        public Task<BitcoinTransaction> SignRedeemTxAsync(
            BitcoinTransaction redeemTx,
            BitcoinTransaction paymentTx,
            WalletAddress redeemAddress,
            byte[] secret,
            byte[] redeemScript)
        {
            return SignHtlcSwapRedeemForP2ShTxAsync(redeemTx, paymentTx, redeemAddress, secret, redeemScript);
        }

        public async Task<BitcoinTransaction> SignHtlcSwapRedeemForP2ShTxAsync(
            BitcoinTransaction tx,
            BitcoinTransaction paymentTx,
            WalletAddress redeemAddress,
            byte[] secret,
            byte[] redeemScript)
        {
            var spentOutput = paymentTx.Outputs
                .Cast<BitcoinTxOutput>()
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
                keyPath: redeemAddress.KeyPath,
                keyType: redeemAddress.KeyType);

            var redeemScriptSig = BitcoinSwapTemplate.CreateSwapRedeemScript(
                sig: signature,
                pubKey: redeemAddressPublicKey.ToUnsecuredBytes(),
                secret: secret,
                redeemScript: redeemScript);

            var redeemScriptSigSegwit = redeemScriptSig.ToWitScript();

            tx.SetSignature(redeemScriptSigSegwit, spentOutput);

            if (!tx.Verify(spentOutput, out var errors, Account.Config.Network))
            {
                Log.Error("Redeem transaction verify errors: {errors}", errors);
                return null;
            }

            return tx;
        }
    }
}