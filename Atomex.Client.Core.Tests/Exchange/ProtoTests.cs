using System.Collections.Generic;
using System.IO;
using Atomex.Api.Proto;
using Atomex.Common;
using Atomex.Core;
using Xunit;

namespace Atomex.Client.Core.Tests
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
                    Assert.Equal(Order.FromWallets[i].Currency.Name, deserializedOrder.FromWallets[i].Currency.Name);
                }

                Assert.Equal(Order.Symbol.Name, deserializedOrder.Symbol.Name);
            }
        }
    }
}