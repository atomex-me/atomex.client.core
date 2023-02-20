//using System.Collections.Generic;
//using System.Linq;
//using Xunit;

//using Atomex.Blockchain.Bitcoin;
//using Atomex.Blockchain.BlockCypher;
//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;

//namespace Atomex.Client.Core.Tests
//{
//    public class BlockCypherApiTests
//    {
//        public static IEnumerable<object[]> AddressTestData => new List<object[]>
//        {
//            new object[]
//            {
//                Common.BtcMainNet, // currency
//                "17c5d61XW7714Abk7yNxKZkRybiCR9ft6m" // address
//            },
//        };

//        [Theory]
//        [MemberData(nameof(AddressTestData))]
//        public async void GetBalanceTest(
//            BitcoinBasedConfig currency,
//            string address)
//        {
//            var api = new BlockCypherApi(currency, BlockCypherApi.BitcoinMainNet);
//            var balanceResult = await api.GetBalanceAsync(address);

//            Assert.False(balanceResult.HasError, balanceResult.Error?.Message ?? "");
//        }

//        public static IEnumerable<object[]> InputTestData => new List<object[]>
//        {
//            new object[]
//            {
//                Common.BtcMainNet, // currency
//                "d4eb2a63e37391d889c0c8cb9d26bd556ba6721558626af6881049e5b1724b41", // txid
//                0, // input index
//                "40cb4c38bccc4cabc9d5cadad5f3d5aea41a277c7c10b839b67a6a54155e8911", // prev txid
//                1 // prev tx output index
//            }
//        };

//        [Theory]
//        [MemberData(nameof(InputTestData))]
//        public async void GetInputTest(
//            BitcoinBasedConfig currency,
//            string txId,
//            uint inputIndex,
//            string prevTxId,
//            uint prevTxOutputIndex)
//        {
//            var api = new BlockCypherApi(currency, BlockCypherApi.BitcoinMainNet);
//            var inputResult = await api.GetInputAsync(txId, inputIndex);

//            Assert.False(inputResult.HasError, inputResult.Error?.Message ?? "");

//            var input = inputResult.Value;

//            Assert.NotNull(input);
//            Assert.Equal(prevTxId, input.Hash);
//            Assert.Equal(prevTxOutputIndex, input.Index);
//        }

//        [Theory]
//        [MemberData(nameof(AddressTestData))]
//        public async void GetUnspentOutputsTest(
//            BitcoinBasedConfig currency,
//            string address)
//        {
//            var api = new BlockCypherApi(currency, BlockCypherApi.BitcoinMainNet);
//            var utxoResult = await api.GetUnspentOutputsAsync(address);

//            Assert.False(utxoResult.HasError, utxoResult.Error?.Message ?? "");

//            var utxo = utxoResult.Value;

//            Assert.NotNull(utxo);
//        }

//        public static IEnumerable<object[]> OutputsTestData => new List<object[]>
//        {
//            new object[]
//            {
//                Common.BtcMainNet, // currency
//                "17c5d61XW7714Abk7yNxKZkRybiCR9ft6m", // address
//                "40cb4c38bccc4cabc9d5cadad5f3d5aea41a277c7c10b839b67a6a54155e8911", // output txid
//                11086888, // output amount
//                "d4eb2a63e37391d889c0c8cb9d26bd556ba6721558626af6881049e5b1724b41" // output spent txid
//            }
//        };

//        [Theory]
//        [MemberData(nameof(OutputsTestData))]
//        public async void GetOutputsTest(
//            BitcoinBasedConfig currency,
//            string address,
//            string outputTxId,
//            long outputAmount,
//            string outputSpentTxId)
//        {
//            var api = new BlockCypherApi(currency, BlockCypherApi.BitcoinMainNet);
//            var outputsResult = await api.GetOutputsAsync(address);

//            Assert.False(outputsResult.HasError, outputsResult.Error?.Message ?? "");

//            var outputs = outputsResult.Value?.ToList();

//            Assert.NotNull(outputs);
//            Assert.True(outputs.Any());
//            Assert.Contains(outputs, o => o.TxId == outputTxId && o.Value == outputAmount &&
//                (outputSpentTxId != null && o.SpentTxPoint != null && o.SpentTxPoint.Hash == outputSpentTxId ||
//                outputSpentTxId == null && o.SpentTxPoint == null));
//        }

//        public static IEnumerable<object[]> TransactionTestData => new List<object[]>
//        {
//            new object[]
//            {
//                Common.BtcMainNet, // currency
//                "d4eb2a63e37391d889c0c8cb9d26bd556ba6721558626af6881049e5b1724b41", // txid
//                1, // inputs count
//                2, // outputs count
//                4775, // fees
//                593212 // blockheight
//            }
//        };

//        [Theory]
//        [MemberData(nameof(TransactionTestData))]
//        public async void GetTransactionTest(
//            BitcoinBasedConfig currency,
//            string txId,
//            int inputsCount,
//            int outputsCount,
//            long fees,
//            int blockHeight)
//        {
//            var api = new BlockCypherApi(currency, BlockCypherApi.BitcoinMainNet);
//            var txResult = await api.GetTransactionAsync(txId);

//            Assert.False(txResult.HasError, txResult.Error?.Message ?? "");

//            var tx = txResult.Value as BitcoinTransaction;

//            Assert.NotNull(tx);
//            Assert.True(tx.Id == txId);
//            Assert.Equal(inputsCount, tx.Inputs.Length);
//            Assert.Equal(outputsCount, tx.Outputs.Length);
//            Assert.Equal(fees, tx.Fees);
//            Assert.Equal(blockHeight, tx.BlockInfo.BlockHeight);
//        }

//        public static IEnumerable<object[]> IsTransactionOutputSpentTestData => new List<object[]>
//        {
//            new object[]
//            {
//                Common.BtcMainNet, // currency
//                "d4eb2a63e37391d889c0c8cb9d26bd556ba6721558626af6881049e5b1724b41", // txid
//                1, // output no
//                "a841db57448bbdd8ffae41d50ac133bfecba1c010fb9e6e1b208b58a363dda2c", // spent tx id
//                0, // spent index
//            }
//        };

//        [Theory]
//        [MemberData(nameof(IsTransactionOutputSpentTestData))]
//        public async void IsTransactionOutputSpentTest(
//            BitcoinBasedConfig currency,
//            string txId,
//            uint outputNo,
//            string spentTxId,
//            uint spentIndex)
//        {
//            var api = new BlockCypherApi(currency, BlockCypherApi.BitcoinMainNet);
//            var spentPointResult = await api.IsTransactionOutputSpent(txId, outputNo);

//            Assert.False(spentPointResult.HasError, spentPointResult.Error?.Message ?? "");

//            var spentPoint = spentPointResult.Value;

//            Assert.NotNull(spentPoint);
//            Assert.Equal(spentTxId, spentPoint.Hash);
//            Assert.Equal(spentIndex, spentPoint.Index);
//        }

//        [Fact]
//        public void BroadcastParseTest()
//        {
//            var response = @"{
//                  ""tx"": {
//                    ""block_height"": -1,
//                    ""block_index"": -1,
//                    ""hash"": ""fe72be5a06f079423a4b6c1697d15fd03c0208752c940aa9434fdf83bfda8142"",
//                    ""addresses"": [
//                      ""1J7jvx5tG7CuvPN1UWNXPUjEKLx5P1wZc2"",
//                      ""18a8hnNbN8dy4ZyspPsvPPCKcmCTrLGyBZ"",
//                      ""1gDMQq8hd6ii79GDQep7KBpSUpL9nPYd2""
//                    ]
//                }";

//            var txResponse = JsonConvert.DeserializeObject<JObject>(response);

//            var hash = txResponse["tx"] is JObject tx && tx.ContainsKey("hash")
//                ? tx.Value<string>("hash")
//                : null;

//            Assert.NotNull(hash);
//        }
//    }
//}