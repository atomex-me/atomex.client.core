using NBitcoin;

using Atomex.Common.Memory;
using Atomex.Blockchain.Bitcoin;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallets.Bitcoin
{
    public class BitcoinConfig : CurrencyConfig
    {
        public Network Network { get; set; }
        public decimal DustInSatoshi { get; set; }
        public BitcoinApiSettings ApiSettings { get; set; }

        public override string AddressFromKey(
            SecureBytes publicKey,
            WalletInfo walletInfo = null)
        {
            var scriptPubKeyType = walletInfo != null
                ? BitcoinWalletType.ToScriptPubKeyType(walletInfo.AdditionalType)
                : ScriptPubKeyType.Legacy;

            return new PubKey(publicKey.ToUnsecuredBytes())
                .GetAddress(scriptPubKeyType, Network)
                .ToString();
        }

        public long CoinToSatoshi(decimal coins) =>
            (long)(coins * DecimalsMultiplier);

        public decimal SatoshiToCoin(long satoshi) =>
            satoshi / DecimalsMultiplier;
    }
}