using NBitcoin;

namespace Atomex.Common
{
    public static class PubKeyExtensions
    {
        public static string GetAddress(
            this PubKey key,
            BitcoinBasedConfig_OLD currency)
        {
            return key.GetAddress(ScriptPubKeyType.Legacy, currency.Network).ToString();
        }

        public static string GetSegwitAddress(
            this PubKey key,
            BitcoinBasedConfig_OLD currency)
        {
            return key.GetAddress(ScriptPubKeyType.Segwit, currency.Network).ToString();
        }
    }
}