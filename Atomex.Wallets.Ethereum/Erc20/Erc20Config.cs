using Nethereum.Signer;

using Atomex.Blockchain.Ethereum.Erc20;
using Atomex.Common.Memory;
using Atomex.Wallets.Abstract;

namespace Atomex.Wallets.Ethereum.Erc20
{
    public class Erc20Config : CurrencyConfig
    {
        public string TokenContract { get; set; }
        public Erc20ApiSettings ApiSettings { get; set; }

        public override string AddressFromKey(
            SecureBytes publicKey,
            WalletInfo walletInfo = null) =>
            new EthECKey(publicKey.ToUnsecuredBytes(), false)
                .GetPublicAddress()
                .ToLowerInvariant();
    }
}