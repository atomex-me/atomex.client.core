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

    public class BitcoinBasedTxOutput
    {
        public Coin Coin { get; }
        public uint Index => Coin.Outpoint.N;
        public long Value => Coin.Amount.Satoshi;
        public bool IsValid => Coin.TxOut.ScriptPubKey.IsValid;
        public string TxId => Coin.Outpoint.Hash.ToString();
        public BitcoinOutputType Type => Coin.GetOutputType();
        public int Confirmations { get; set; }
        public bool IsConfirmed => Confirmations > 0;
        public ITxPoint SpentTxPoint { get; set; }
        public bool IsSpent => SpentTxPoint != null;
        public int SpentTxConfirmations { get; set; }
        public bool IsSpentTxConfirmed => SpentTxConfirmations > 0;

        public BitcoinBasedTxOutput(Coin coin)
            : this(coin, confirmations: 0, spentTxPoint: null, spentTxConfirmations: 0)
        {
        }

        public BitcoinBasedTxOutput(Coin coin, int confirmations, ITxPoint spentTxPoint, int spentTxConfirmations)
        {
            Coin = coin;
            Confirmations = confirmations;
            SpentTxPoint = spentTxPoint;
            SpentTxConfirmations = spentTxConfirmations;
        }

        public bool IsP2Sh =>
            Coin.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2SH);

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