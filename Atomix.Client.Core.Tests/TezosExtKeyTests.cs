using System.Text;
using Atomix.Wallet.Tezos;
using NBitcoin;
using Xunit;

namespace Atomix.Client.Core.Tests
{
    public class TezosExtKeyTests
    {
        private const string Message = "I LOVE TEZOS";

        private const string Mnemonic =
            "return auction present awesome blast excess receive obtain explain spider iron hip curtain recipe tent aim bonus hip cliff shrug lyrics pass right spend";

        [Fact]
        public void TrustWalletTezosExtKeyTest()
        {
            var messageBytes = Encoding.UTF8.GetBytes(Message);

            var seed = new Mnemonic(Mnemonic).DeriveSeed();

            var extKey = new TrustWalletTezosExtKey(seed);

            var childKey = extKey.Derive(new KeyPath("m/44'/1729'/0'/0'"));
            childKey.GetPublicKey(out var childPublicKey);

            var signature = childKey.SignMessage(messageBytes);
            Assert.True(childKey.VerifyMessage(messageBytes, signature));

            var address = Common.CurrenciesTestNet.Get<Tezos>().AddressFromKey(childPublicKey);
            Assert.NotNull(address);
        }

        [Fact]
        public void TezosExtKeyTest()
        {
            var messageBytes = Encoding.UTF8.GetBytes(Message);

            var seed = new Mnemonic(Mnemonic).DeriveSeed();

            var extKey = new TezosExtKey(seed);

            var childKey = extKey.Derive(new KeyPath("m/44'/1729'/0'/0'"));
            childKey.GetPublicKey(out var childPublicKey);

            var signature = childKey.SignMessage(messageBytes);
            Assert.True(childKey.VerifyMessage(messageBytes, signature));

            var address = Common.CurrenciesTestNet.Get<Tezos>().AddressFromKey(childPublicKey);
            Assert.NotNull(address);
        }

        [Fact]
        public void TezosExtKeyDerivationTest()
        {
            var messageBytes = Encoding.UTF8.GetBytes(Message);

            var seed = new Mnemonic(Mnemonic).DeriveSeed();

            var extKey = new TezosExtKey(seed);

            for (var i = 0; i < 100; ++i)
            {
                var childKey = extKey.Derive(new KeyPath($"m/44'/1729'/0'/0/{i}"));
                childKey.GetPublicKey(out var childPublicKey);

                var signature = childKey.SignMessage(messageBytes);
                Assert.True(childKey.VerifyMessage(messageBytes, signature));

                var address = Common.CurrenciesTestNet.Get<Tezos>().AddressFromKey(childPublicKey);
                Assert.NotNull(address);
            }
        }
    }
}