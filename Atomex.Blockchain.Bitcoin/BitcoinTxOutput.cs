using System.Collections.Generic;
using System.Linq;

using NBitcoin;

using Atomex.Blockchain.Bitcoin.Common;

namespace Atomex.Blockchain.Bitcoin
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

    public class BitcoinTxOutput
    {
        public Coin Coin { get; set; }
        public string Currency { get; set; }
        public uint Index => Coin.Outpoint.N;
        public long Value => Coin.Amount.Satoshi;
        public bool IsValid => Coin.TxOut.ScriptPubKey.IsValid;
        public string TxId => Coin.Outpoint.Hash.ToString();
        public BitcoinOutputType Type => Coin.GetOutputType();
        public List<BitcoinTxPoint>? SpentTxPoints { get; set; }
        public bool IsConfirmed { get;set; }
        public bool IsSpent => SpentTxPoints?.Any() ?? false;
        public bool IsSpentConfirmed { get; set; }

        public bool IsP2Sh =>
            Coin.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2SH);

        public bool IsPayToScriptHash(Script redeemScript) =>
            IsP2Sh && redeemScript.PaymentScript.Equals(Coin.TxOut.ScriptPubKey);

        public bool IsPayToScriptHash(byte[] redeemScript) =>
            IsP2Sh && new Script(redeemScript).PaymentScript.Equals(Coin.TxOut.ScriptPubKey);

        public bool IsSegWit => Coin.GetHashVersion() == HashVersion.WitnessV0;

        public string? DestinationAddress(Network network) => Coin.GetAddressOrDefault(network);
    }
}