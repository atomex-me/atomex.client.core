using System.Collections.Generic;
using System.IO;
using Atomix.Api.Proto;
using Atomix.Common;
using Atomix.Core.Entities;
using Xunit;

namespace Atomix.Client.Core.Tests
{
    public class ProtoTests
    {
        private ProtoSchemes Schemes { get; } = new ProtoSchemes(
            currencies: Common.CurrenciesTestNet,
            symbols: Common.SymbolsTestNet);

        private Order Order { get; } = new Order
        {
            Symbol = Common.LtcBtcTestNet,
            FromWallets = new List<WalletAddress>
            {
                new WalletAddress
                {
                    Address = "abcdefg",
                    Currency = Common.BtcTestNet
                }, new WalletAddress
                {
                    Address = "gfedcba",
                    Currency = Common.BtcTestNet
                }
            }
        };

        [Fact]
        public void OrderSendSchemeTest()
        {
            var serialized = Schemes.OrderSend.SerializeWithMessageId(Order);

            using (var stream = new MemoryStream(serialized, 1, serialized.Length - 1))
            {
                var deserializedOrder = Schemes.OrderSend
                    .DeserializeWithLengthPrefix(stream);

                deserializedOrder.ResolveRelationshipsByName(Common.CurrenciesTestNet, Common.SymbolsTestNet);

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
}