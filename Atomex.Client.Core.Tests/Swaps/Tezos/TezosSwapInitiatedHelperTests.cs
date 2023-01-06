using System;

using Xunit;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Tezos.Helpers;

namespace Atomex.Client.Core.Tests
{
    public class TezosSwapInitiatedHelperTests
    {
        [Fact]
        public async void CanFindTezosInitiateTestAsync()
        {
            var swap = new Swap
            {
                PaymentTx            = new TezosOperation { From = "tz1aKTCbAUuea2RV9kxqRVRg3HT7f1RKnp6a" },
                IsInitiative         = true,
                TimeStamp            = new DateTime(637778613995020000),
                Side                 = Side.Sell,
                Qty                  = 33.707563m,
                Price                = 0.02964706m,
                PartyRewardForRedeem = 0.004364m,
                PartyAddress         = "tz1g7tbzDQ3F63UTD1t5J7Hp9jTRdfC24BnK",
                SecretHash           = Hex.FromString("ca94a4eb75540fffd89658e1766af008cde7ba98c1b810886823302c64872fbf"),
                MakerNetworkFee      = 0,
            };

            var result = await TezosSwapInitiatedHelper.TryToFindPaymentAsync(
                swap: swap,
                currency: Common.XtzMainNet);

            Assert.Null(result.Error);
            //Assert.NotNull(result.Value);
        }
    }
}