using NBitcoin;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.BitcoinBased
{
    public enum BitcoinOutputType
    {
        NonStandard,
        MultiSig,
        WitnessMultiSig,
        P2PK,
        P2PKH,
        P2SH,
        P2WPKH,
        P2WSH
    }

    public class BitcoinBasedTxOutput : ITxOutput
    {
        public Coin Coin { get; }
        public uint Index => Coin.Outpoint.N;
        public long Value => Coin.Amount.Satoshi;
        public bool IsValid => Coin.TxOut.ScriptPubKey.IsValid;
        public string TxId => Coin.Outpoint.Hash.ToString();
        public bool IsSpent => SpentTxPoint != null;
        public ITxPoint SpentTxPoint { get; set; }
        public BitcoinOutputType Type => Coin.GetOutputType();

        public BitcoinBasedTxOutput(Coin coin)
            : this(coin, null)
        {
        }

        public BitcoinBasedTxOutput(Coin coin, ITxPoint spentTxPoint)
        {
            Coin = coin;
            SpentTxPoint = spentTxPoint;
        }

        public bool IsP2Pk =>
            Coin.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2PK);

        public bool IsP2Pkh =>
            Coin.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2PKH);

        public bool IsSegwitP2Pkh =>
            Coin.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2WPKH);

        public bool IsP2Sh =>
            Coin.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2SH);

        public bool IsP2PkhSwapPayment =>
            BitcoinBasedSwapTemplate.IsP2PkhSwapPayment(Coin.TxOut.ScriptPubKey);

        public bool IsHtlcP2PkhSwapPayment =>
            BitcoinBasedSwapTemplate.IsHtlcP2PkhSwapPayment(Coin.TxOut.ScriptPubKey);

        public bool IsPayToScriptHash(Script redeemScript) =>
            IsP2Sh && redeemScript.PaymentScript.Equals(Coin.TxOut.ScriptPubKey);

        public bool IsPayToScriptHash(byte[] redeemScript) =>
            IsP2Sh && new Script(redeemScript).PaymentScript.Equals(Coin.TxOut.ScriptPubKey);

        public bool IsSegWit => Coin.ScriptPubKey.IsScriptType(ScriptType.Witness);

        public string DestinationAddress(Network network) =>
            Coin.TxOut.ScriptPubKey
                .GetDestinationAddress(network)
                .ToString();
    }
}