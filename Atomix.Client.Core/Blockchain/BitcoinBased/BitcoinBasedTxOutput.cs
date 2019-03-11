using Atomix.Blockchain.Abstract;
using Atomix.Core.Entities;
using NBitcoin;

namespace Atomix.Blockchain.BitcoinBased
{
    public class BitcoinBasedTxOutput : ITxOutput
    {
        public ICoin Coin { get; }
        public uint Index => Coin.Outpoint.N;
        public long Value => ((Money) Coin.Amount).Satoshi;
        public bool IsValid => Coin.TxOut.ScriptPubKey.IsValid;
        public string TxId => Coin.Outpoint.Hash.ToString();
        public bool IsSpent => SpentTxPoint != null;
        public ITxPoint SpentTxPoint { get; set; }

        public BitcoinBasedTxOutput(ICoin coin)
            : this(coin, null)
        {
        }

        public BitcoinBasedTxOutput(ICoin coin, ITxPoint spentTxPoint)
        {
            Coin = coin;
            SpentTxPoint = spentTxPoint;
        }

        public bool IsP2Pkh => Coin.TxOut.ScriptPubKey.FindTemplate() == PayToPubkeyHashTemplate.Instance;

        public bool IsSegwitP2Pkh => Coin.TxOut.ScriptPubKey.FindTemplate() == PayToWitPubKeyHashTemplate.Instance;

        public bool IsP2PkhSwapPayment => BitcoinBasedSwapTemplate.IsP2PkhSwapPayment(Coin.TxOut.ScriptPubKey);

        public bool IsSwapPayment => IsP2PkhSwapPayment;

        public string DestinationAddress(Currency currency)
        {
            return Coin.TxOut.ScriptPubKey
                .GetDestinationAddress(((BitcoinBasedCurrency) currency).Network)
                .ToString();
        }

        public BitcoinBasedTxOutput WithPatchedTxId(string txId)
        {
            return new BitcoinBasedTxOutput(new Coin
            {
                Amount = new Money(Value),
                Outpoint = new OutPoint(new uint256(txId), Coin.Outpoint.N),
                ScriptPubKey = Coin.TxOut.ScriptPubKey,
                TxOut = Coin.TxOut.Clone()
            });
        }
    }
}