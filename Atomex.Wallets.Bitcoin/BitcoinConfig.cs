using System.Numerics;

using NBitcoin;

using Atomex.Common;
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

        public BigInteger CoinToSatoshi(decimal coins) =>
            coins.Multiply(BigInteger.Pow(10, Decimals));

        public decimal SatoshiToCoin(BigInteger satoshi) =>
            satoshi.ToDecimal(Decimals, Decimals);
    }
}