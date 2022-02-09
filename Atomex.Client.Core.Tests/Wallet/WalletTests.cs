using System;
using System.Security;

using NBitcoin;
using Xunit;

using Atomex.Wallet;
using Network = Atomex.Core.Network;

namespace Atomex.Client.Core.Tests
{
    public class WalletTests
    {
        [Fact]
        public void GetDeterministicSecretTest()
        {
            var mnemonic = new Mnemonic(Wordlist.English, WordCount.TwentyFour);

            var wallet = new HdWallet(mnemonic.ToString(), Wordlist.English, new SecureString(), Network.TestNet);

            var timeStamp = DateTime.UtcNow;

            var secretBtc1 = wallet.GetDeterministicSecret(Common.BtcTestNet, timeStamp);
            var secretBtc2 = wallet.GetDeterministicSecret(Common.BtcTestNet, timeStamp.AddMilliseconds(1));
            Assert.NotEqual(secretBtc1, secretBtc2);

            var secretBtc3 = wallet.GetDeterministicSecret(Common.BtcTestNet, timeStamp);
            Assert.Equal(secretBtc1, secretBtc3);

            var secretLtc1 = wallet.GetDeterministicSecret(Common.LtcTestNet, timeStamp);
            Assert.NotEqual(secretBtc1, secretLtc1);

            var secretXtz1 = wallet.GetDeterministicSecret(Common.XtzTestNet, timeStamp);
            Assert.NotEqual(secretBtc1, secretXtz1);

            var secretEth1 = wallet.GetDeterministicSecret(Common.EthTestNet, timeStamp);
            Assert.NotEqual(secretBtc1, secretEth1);
        }
    }
}