using System;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Swaps.Abstract;
using NBitcoin;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class BitcoinBasedSwapTemplateTests
    {
        private const string SwapRedeemScript = "304402205552bda7a0a46e37e746ddb12672465c" +
                                                "9629f5b6bd7260d3dbdee07e738827d8022013e6" +
                                                "55188e081612806cd0a749120725d9cf357094db" +
                                                "658909e8c01c65151e0101 0305cc77835a82018" +
                                                "1bb9b5fa61c831cf2c33712894abad968522fa3b" +
                                                "8eab1ba82 33aa39328c4ebe288ed671c8d5227e" +
                                                "92 0";

        private const string SwapSecretHash = "bxa0lTNfnaKRPTfu6mwCmPYtQ+4=";

        [Fact]
        public void ExtractSecretTest()
        {
            var redeemScript = new Script(SwapRedeemScript);

            Assert.NotNull(redeemScript);

            var secret = BitcoinBasedSwapTemplate.ExtractSecretFromP2PkhSwapRedeem(redeemScript);

            Assert.NotNull(secret);

            var secretHash = Convert.ToBase64String(CurrencySwap.CreateSwapSecretHash160(secret));

            Assert.Equal(SwapSecretHash, secretHash);
        }
    }
}