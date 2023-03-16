using System;

using NBitcoin;

using Atomex.Blockchain.Bitcoin.Abstract;
using Atomex.Blockchain.Bitcoin.Common;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinInputToSign
    {
        /// <summary>
        /// Reference to tx output
        /// </summary>
        public BitcoinTxOutput Output { get; set; }
        /// <summary>
        /// Known redeem script
        /// </summary>
        public Script KnownRedeemScript { get; set; }
        /// <summary>
        /// Path to key which can be used for signing
        /// </summary>
        public string KeyPath { get; set; }
        /// <summary>
        /// Signature hash type
        /// </summary>
        public SigHash SigHash { get; set; } = SigHash.All;
        /// <summary>
        /// Custom signer for nonstandard outputs
        /// </summary>
        public IBitcoinOutputSigner Signer { get; set; }
        /// <summary>
        /// Input sequence number (used if greater than zero)
        /// </summary>
        public uint Sequence { get; set; }

        public virtual Script CreateSignatureScript(byte[] signature, byte[] publicKey) =>
            Output.Type switch
            {
                BitcoinOutputType.P2PKH => PayToPubkeyHashTemplate.Instance.GenerateScriptSig(
                    signature: new TransactionSignature(signature, SigHash),
                    publicKey: new PubKey(publicKey)),

                BitcoinOutputType.P2WPKH => PayToWitPubKeyHashTemplate.Instance.GenerateWitScript(
                    signature: new TransactionSignature(signature, SigHash),
                    publicKey: new PubKey(publicKey)),

                BitcoinOutputType.P2PK => PayToPubkeyTemplate.Instance.GenerateScriptSig(
                    signature: new TransactionSignature(signature, SigHash)),

                _ => Signer?.CreateSignatureScript(signature, publicKey, KnownRedeemScript)
                    ?? throw new Exception("Signer not specified for P2SH or P2WSH output.")
            };

        public virtual int SizeWithSignature()
        {
            var size = 36; // outpoint

            if (Output.IsSegWit)
            {
                size += 1 // scriptSig length compact size 
                    + 0   // scriptSig
                    + 4;  // nSequence

                if (Output.Type == BitcoinOutputType.P2WPKH)
                {
                    size += 107 / 4; // witness data
                }
                else // p2wsh
                {
                    size += Signer.SignatureSize() / 4; // witness data
                }
            }
            else
            {
                if (Output.Type == BitcoinOutputType.P2PKH)
                {
                    size += 1 // scriptSig length compact size
                        + 107 // scriptSig
                        + 4;  // nSequence
                }
                else // p2sh
                {
                    size += Signer.SignatureSize().CompactSize() // scriptSig length compact size
                        + Signer.SignatureSize()                 // scriptSig
                        + 4;                                     // nSequence
                }
            }

            return size;
        }
    }
}