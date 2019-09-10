using NBitcoin;

namespace Atomex.Common
{
    public static class PubKeyExtensions
    {
        public static string GetAddress(
            this PubKey key,
            BitcoinBasedCurrency currency)
        {
            return key.GetAddress(ScriptPubKeyType.Legacy ,currency.Network).ToString();
        }

        public static string GetSegwitAddress(
            this PubKey key,
            BitcoinBasedCurrency currency)
        {
            return key.GetSegwitAddress(currency.Network).ToString();
        }
    }
}