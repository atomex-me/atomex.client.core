using System.Threading.Tasks;

using Xunit;

using Atomex.Blockchain.Tezos.Tzkt;

namespace Atomex.Tests.Tezos
{
    public class TzktApiTests
    {
        private TzktApi _api;
        private TzktApi CreateApi() => _api ??= new TzktApi(new TzktSettings());

        [Fact]
        public async Task CanGetOperationsByAddressAsync()
        {
            var api = CreateApi();

            var (ops, error) = await api.GetOperationsByAddressAsync(
                address: "tz1RRBkMhfNTj6EbhJoHHjgpM1oP7N4rx41N",
                parameter: null);

            Assert.Null(error);
            Assert.NotNull(ops);
            Assert.NotEmpty(ops);
        }

        [Fact]
        public async Task CanGetTransactionAsync()
        {
            var api = CreateApi();

            var (tx, error) = await api.GetTransactionAsync(
                txId: "onivEthGz3j2jSJAU6gnxVyQ7w5Aeu2CgS2bCxCPf62fysu1w92");
                //txId: "ooSLa53hEc35HXVKamghCAfGr4iXBki7yhHdn3Th7w4wzEMMjys");

            Assert.Null(error);
            Assert.NotNull(tx);
        }
    }
}