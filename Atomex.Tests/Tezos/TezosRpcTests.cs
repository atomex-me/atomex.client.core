using System.Text.Json;
using System.Threading.Tasks;

using Xunit;

using Atomex.Blockchain.Tezos;

namespace Atomex.Tests.Tezos
{
    public class TezosRpcTests
    {
        private TezosRpc _rpc;
        private TezosRpc CreateRpc() => _rpc ??= new TezosRpc(new TezosRpcSettings() { Url = "https://pcr.tzkt.io/mainnet/" });

        [Fact]
        public async Task CanGetManagerKeyAsync()
        {
            var rpc = CreateRpc();

            var managerKeyJson = await rpc
                .GetManagerKeyAsync("tz1aKTCbAUuea2RV9kxqRVRg3HT7f1RKnp6a")
                .ConfigureAwait(false);

            var managerKey = JsonSerializer.Deserialize<JsonElement>(managerKeyJson);

            Assert.Equal("edpktzA3wYpNe9vG2v4Hy3oftUxEhJ4V8h6kXUN74qNN9fPiNbQ5oP", managerKey.GetString());
        }

        [Fact]
        public async Task CanGetAccountAsync()
        {
            var rpc = CreateRpc();

            var account = await rpc
                .GetAccountAsync("tz1aKTCbAUuea2RV9kxqRVRg3HT7f1RKnp6a")
                .ConfigureAwait(false);

            Assert.NotNull(account);
        }
    }
}