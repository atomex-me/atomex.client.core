using System.Collections.Generic;
using System.IO;
using Atomex.Api.Proto;
using Atomex.Core;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class ProtoTests
    {
        private ProtoSchemes Schemes { get; } = new ProtoSchemes();

        private Order Order { get; } = new Order
        {
            Symbol = Common.LtcBtcTestNet.Name,
            FromWallets = new List<WalletAddress>
            {
                new WalletAddress
                {
                    Address = "abcdefg",
                    Currency = Common.BtcTestNet.Name
                }, new WalletAddress
                {
                    Address = "gfedcba",
                    Currency = Common.BtcTestNet.Name
                }
            }
        };

        [Fact]
        public void OrderSendSchemeTest()
        {
            var serialized = Schemes.OrderSend.SerializeWithMessageId(Order);

            using var stream = new MemoryStream(serialized, 1, serialized.Length - 1);

            var deserializedOrder = Schemes.OrderSend
                .DeserializeWithLengthPrefix(stream);

            for (var i = 0; i < Order.FromWallets.Count; ++i)
            {
                Assert.NotNull(deserializedOrder.FromWallets[i]);
                Assert.Equal(Order.FromWallets[i].Address, deserializedOrder.FromWallets[i].Address);
                Assert.Equal(Order.FromWallets[i].Currency, deserializedOrder.FromWallets[i].Currency);
            }

            Assert.Equal(Order.Symbol, deserializedOrder.Symbol);
        }
    }
}