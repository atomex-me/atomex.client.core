using Nethereum.Signer;

using Atomex.Blockchain.Ethereum;
using Atomex.Common.Memory;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallets.Ethereum
{
    public class EthereumConfig : CurrencyConfig
    {
        public Chain Chain { get;  set; }
        public EthereumApiSettings ApiSettings { get; set; }
        public decimal DefaultGasPrice { get; set; }

        public override string AddressFromKey(
            SecureBytes publicKey,
            WalletInfo walletInfo = null) =>
            new EthECKey(publicKey.ToUnsecuredBytes(), false)
                .GetPublicAddress()
                .ToLowerInvariant();
    }
}