using System.Text;

using NBitcoin;
using Xunit;

using Atomex.Common.Memory;
using Atomex.Wallet.BitcoinBased;

namespace Atomex.Client.Core.Tests
{
    public class BitcoinBasedExtKeyTests
    {
        private const string Message = "I LOVE BITCOIN";

        private const string Mnemonic =
            "return auction present awesome blast excess receive obtain explain spider iron hip curtain recipe tent aim bonus hip cliff shrug lyrics pass right spend";

        [Fact]
        public void BitcoinBasedExtKeyTest()
        {
            var messageBytes = Encoding.UTF8.GetBytes(Message);

            using var seed = new SecureBytes(new Mnemonic(Mnemonic).DeriveSeed());
            using var extKey = new BitcoinBasedExtKey(seed);
            using var childKey = extKey.Derive(new KeyPath("m/44'/0'/0'/0'"));
            using var secureChildPublicKey = childKey.GetPublicKey();
            var childPublicKey = secureChildPublicKey.ToUnsecuredBytes();

            var signature = childKey.SignMessage(messageBytes);
            Assert.True(childKey.VerifyMessage(messageBytes, signature));

            var address = Common.CurrenciesTestNet.Get<BitcoinConfig>("BTC").AddressFromKey(childPublicKey);
            Assert.NotNull(address);
        }
    }
}