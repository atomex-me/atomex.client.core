using System.Text.Json;
using System.Threading.Tasks;

using Xunit;

using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Common;
using Atomex.Cryptography;

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

        [Fact]
        public async Task CanGetFa12AllowanceAsync()
        {
            var rpc = CreateRpc();

            var (allowance, error) = await rpc
                .GetFa12AllowanceAsync(
                    holderAddress: "tz1aKTCbAUuea2RV9kxqRVRg3HT7f1RKnp6a",
                    spenderAddress: "KT1Ap287P1NzsnToSJdA4aqSNjPomRaHBZSr",
                    callingAddress: "tz1aKTCbAUuea2RV9kxqRVRg3HT7f1RKnp6a",
                    tokenContractAddress: "KT1PWx2mnDueood7fEmfbBDKx1D9BAnnXitn",
                    tokenViewContractAddress: "KT1TgnUythoUoLKxCCEdR1VkjiiY5TmE7M7r",
                    publicKey: Base58Check.Decode("edpktzA3wYpNe9vG2v4Hy3oftUxEhJ4V8h6kXUN74qNN9fPiNbQ5oP", TezosPrefix.Edpk).ToArray(),
                    settings: new TezosFillOperationSettings())
                .ConfigureAwait(false);

            Assert.Null(error);
        }

        [Fact]
        public async Task CanGetFa12TotalSupplyAsync()
        {
            var rpc = CreateRpc();

            var (totalSupply, error) = await rpc
                .GetFa12TotalSupply(
                    callingAddress: "tz1aKTCbAUuea2RV9kxqRVRg3HT7f1RKnp6a",
                    tokenContractAddress: "KT1PWx2mnDueood7fEmfbBDKx1D9BAnnXitn",
                    tokenViewContractAddress: "KT1TgnUythoUoLKxCCEdR1VkjiiY5TmE7M7r",
                    publicKey: Base58Check.Decode("edpktzA3wYpNe9vG2v4Hy3oftUxEhJ4V8h6kXUN74qNN9fPiNbQ5oP", TezosPrefix.Edpk).ToArray(),
                    settings: new TezosFillOperationSettings())
                .ConfigureAwait(false);

            Assert.Null(error);
            Assert.True(totalSupply > 0);
        }
    }
}