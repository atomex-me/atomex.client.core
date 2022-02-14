using NBitcoin;

using Atomex.Blockchain.BitcoinBased;

namespace Atomex.Common
{
    public static class CoinExtensions
    {
        public static BitcoinOutputType GetOutputType(this Coin coin)
        {
            if (coin.ScriptPubKey.IsScriptType(ScriptType.P2PKH))
                return BitcoinOutputType.P2PKH;

            if (coin.ScriptPubKey.IsScriptType(ScriptType.P2WPKH))
                return BitcoinOutputType.P2WPKH;

            if (coin.ScriptPubKey.IsScriptType(ScriptType.P2SH))
                return BitcoinOutputType.P2SH;

            if (coin.ScriptPubKey.IsScriptType(ScriptType.P2WSH))
                return BitcoinOutputType.P2WSH;

            if (coin.ScriptPubKey.IsScriptType(ScriptType.P2PK))
                return BitcoinOutputType.P2PK;

            if (coin.ScriptPubKey.IsScriptType(ScriptType.MultiSig))
                return coin.ScriptPubKey.IsScriptType(ScriptType.Witness)
                    ? BitcoinOutputType.WitnessMultiSig
                    : BitcoinOutputType.MultiSig;

            return BitcoinOutputType.NonStandard;
        }
    }
}