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

        public int WalletId { get; set; }
        public string Currency { get; set; }
        public string Address { get; set; }
        public string KeyPath { get; set; }
        public BitcoinOutputType Type => Coin.GetOutputType();
        public uint Index => Coin.Outpoint.N;
        public long Value => Coin.Amount?.Satoshi ?? 0;
        public string TxId => Coin.Outpoint.Hash.ToString();
        public bool IsConfirmed { get; set; }
        public bool IsSpent => SpentTxPoints?.Any() ?? false;
        public bool IsSpentConfirmed { get; set; }
        public List<BitcoinTxPoint> SpentTxPoints { get; set; }
        public bool IsSegWit => Coin.GetHashVersion() == HashVersion.WitnessV0;
    }
}