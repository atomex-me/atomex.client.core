using Atomex.Blockchain.Abstract;
using Atomex.Core.Entities;
using NBitcoin;

namespace Atomex.Blockchain.BitcoinBased
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

        public bool IsP2Pk => Coin.TxOut.ScriptPubKey.FindTemplate() == PayToPubkeyTemplate.Instance;

        public bool IsP2Pkh => Coin.TxOut.ScriptPubKey.FindTemplate() == PayToPubkeyHashTemplate.Instance;

        public bool IsSegwitP2Pkh => Coin.TxOut.ScriptPubKey.FindTemplate() == PayToWitPubKeyHashTemplate.Instance;

        public bool IsP2Sh => Coin.TxOut.ScriptPubKey.FindTemplate() == PayToScriptHashTemplate.Instance;

        public bool IsSegwitP2Sh => Coin.TxOut.ScriptPubKey.FindTemplate() == PayToWitScriptHashTemplate.Instance;

        public bool IsP2PkhSwapPayment => BitcoinBasedSwapTemplate.IsP2PkhSwapPayment(Coin.TxOut.ScriptPubKey);

        public bool IsHtlcP2PkhSwapPayment => BitcoinBasedSwapTemplate.IsHtlcP2PkhSwapPayment(Coin.TxOut.ScriptPubKey);

        public bool IsPayToScriptHash(Script redeemScript)
        {
            return IsP2Sh && redeemScript.PaymentScript.Equals(Coin.TxOut.ScriptPubKey);
        }

        public bool IsPayToScriptHash(byte[] redeemScript)
        {
            return IsP2Sh && new Script(redeemScript).PaymentScript.Equals(Coin.TxOut.ScriptPubKey);
        }

        public string DestinationAddress(Currency currency)
        {
            return Coin.TxOut.ScriptPubKey
                .GetDestinationAddress(((BitcoinBasedCurrency) currency).Network)
                .ToString();
        }
    }
}