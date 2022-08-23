using System;

using Xunit;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Tezos.Fa12.Helpers;

namespace Atomex.Client.Core.Tests
{
    public class Fa12SwapInitiatedHelperTests
    {
        [Fact]
        public async void CanFindTzbtcInitiateTestAsync()
        {
            var swap = new Swap
            {
                PaymentTx            = new TezosTransaction { From = "tz1aKTCbAUuea2RV9kxqRVRg3HT7f1RKnp6a" },
                IsInitiative         = true,
                TimeStamp            = new DateTime(637719808816320000),
                Side                 = Side.Buy,
                Qty                  = 2.476000088m,
                Price                = 0.07092383m,
                PartyRewardForRedeem = 0.00000368m,
                PartyAddress         = "tz1NDbWscowat4YkYoGmwKxZMFF4BRUQ6ZAW",
                SecretHash           = Hex.FromString("db743389701465c4bc399ece0bc2cfaebbf7638c552f70dd307350fd2cb4281f"),
                MakerNetworkFee      = 0,
            };

            var result = await Fa12SwapInitiatedHelper.TryToFindPaymentAsync(
                swap: swap,
                currency: Common.TzbtcMainNet);

            Assert.Null(result.Error);
            //Assert.NotNull(result.Value);
        }
    }
}