using System.Collections.Generic;
using System.Linq;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.Insight;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class InsightApiTests
    {
        public static IEnumerable<object[]> AddressTestData => new List<object[]>
        {
            new object[]
            {
                Common.BtcMainNet, // currency
                InsightApi.InsightBitPayMainNet, // base uri
                "17c5d61XW7714Abk7yNxKZkRybiCR9ft6m" // address
            },
            //new object[]
            //{
            //    Common.BtcTestNet, // currency
            //    InsightApi.InsightBitPayTestNet, // base uri
            //    "2MyUqSDvEyhiJmQSWBfyAbdeJyBiDnsFkKn" // address
            //},
            new object[]
            {
                Common.LtcMainNet, // currency
                InsightApi.InsightLiteCoreMainNet, // base uri
                "MJBhQat8BdCZC4s5U7zVqTmNErQznD6jJg" // address
            },
            new object[]
            {
                Common.LtcTestNet, // currency
                InsightApi.InsightLiteCoreTestNet, // base uri
                "QbrPfc6DbHqWcpBmo6XAXEp637pX7MowHk", // address
            }
        };

        [Theory]
        [MemberData(nameof(AddressTestData))]
        public async void GetBalanceTest(
            BitcoinBasedCurrency currency,
            string baseUri,
            string address)
        {
            var api = new InsightApi(currency, baseUri);
            var balanceResult = await api.GetBalanceAsync(address);

            Assert.False(balanceResult.HasError, balanceResult.Error?.Description ?? "");
        }

        public static IEnumerable<object[]> InputTestData => new List<object[]>
        {
            new object[]
            {
                Common.BtcMainNet, // currency
                InsightApi.InsightBitPayMainNet, // base uri
                "d4eb2a63e37391d889c0c8cb9d26bd556ba6721558626af6881049e5b1724b41", // txid
                0, // input index
                "40cb4c38bccc4cabc9d5cadad5f3d5aea41a277c7c10b839b67a6a54155e8911", // prev txid
                1 // prev tx output index
            },
            //new object[]
            //{
            //    Common.BtcTestNet, // currency
            //    InsightApi.InsightBitPayTestNet, // base uri
            //    "5c9cdb6af858512cc5b3c7d9ff1bb45d80ee5f885b06544cefe41681f062f76d", // txid
            //    0, // input index
            //    "ce4e2aee1e9789c769ea7110db9f0ab2fc5de3785650ce91f1252fa39bf56088", // prev txid
            //    1 // prev tx output index
            //},
            new object[]
            {
                Common.LtcMainNet, // currency
                InsightApi.InsightLiteCoreMainNet, // base uri
                "4764ef25642647206217002f18fecaece0fd5daba38620e5420200567f7ac9de", // txid
                1, // input index
                "26a96e2e6c84922419abbb220740156fc8f3e7be1460fc136b42ea28d28f31f4", // prev txid
                0 // prev tx output index
            },
            new object[]
            {
                Common.LtcTestNet, // currency
                InsightApi.InsightLiteCoreTestNet, // base uri
                "5de6dc17e0d640d4365c29d73e1052979397a968cffa8e42d03736ffe06e6ef5", // txid
                1, // input index
                "6708a8490ab24f696bb19bf85d3f5f47188d64507bf856fa753453f77e4654de", // prev txid
                1 // prev tx output index
            }
        };

        [Theory]
        [MemberData(nameof(InputTestData))]
        public async void GetInputTest(
            BitcoinBasedCurrency currency,
            string baseUri,
            string txId,
            uint inputIndex,
            string prevTxId,
            uint prevTxOutputIndex)
        {
            var api = new InsightApi(currency, baseUri);
            var inputResult = await api.GetInputAsync(txId, inputIndex);

            Assert.False(inputResult.HasError, inputResult.Error?.Description ?? "");

            var input = inputResult.Value;

            Assert.NotNull(input);
            Assert.Equal(prevTxId, input.Hash);
            Assert.Equal(prevTxOutputIndex, input.Index);
        }

        [Theory]
        [MemberData(nameof(AddressTestData))]
        public async void GetUnspentOutputsTest(
            BitcoinBasedCurrency currency,
            string baseUri,
            string address)
        {
            var api = new InsightApi(currency, baseUri);
            var utxoResult = await api.GetUnspentOutputsAsync(address);

            Assert.False(utxoResult.HasError, utxoResult.Error?.Description ?? "");

            var utxo = utxoResult.Value;

            Assert.NotNull(utxo);
        }

        public static IEnumerable<object[]> OutputsTestData => new List<object[]>
        {
            new object[]
            {
                Common.BtcMainNet, // currency
                InsightApi.InsightBitPayMainNet, // base uri
                "17c5d61XW7714Abk7yNxKZkRybiCR9ft6m", // address
                "40cb4c38bccc4cabc9d5cadad5f3d5aea41a277c7c10b839b67a6a54155e8911", // output txid
                11086888, // output amount
                "d4eb2a63e37391d889c0c8cb9d26bd556ba6721558626af6881049e5b1724b41" // output spent txid
            },
            //new object[]
            //{
            //    Common.BtcTestNet, // currency
            //    InsightApi.InsightBitPayTestNet, // base uri
            //    "2MyUqSDvEyhiJmQSWBfyAbdeJyBiDnsFkKn", // address
            //    "8329821b8c77eff08b928b02a6d83d0102b615cf7c2d0a89a96f0a7647f6a3d9", // output txid
            //    1098605, // output amount
            //    "1e606a544785665544f0ad1bdcc0047d3dfdb0e6006729d0138cfe072781358a" // output spent txid
            //},
            new object[]
            {
                Common.LtcMainNet, // currency
                InsightApi.InsightLiteCoreMainNet, // base uri
                "MJBhQat8BdCZC4s5U7zVqTmNErQznD6jJg", // address
                "6f662717f2f00090d4771b903dc9774faf5b71ccd04fb81c1d3c9bc38dc55e00", // output txid
                2566447, // output amount
                "4764ef25642647206217002f18fecaece0fd5daba38620e5420200567f7ac9de" // output spent txid
            },
            new object[]
            {
                Common.LtcTestNet, // currency
                InsightApi.InsightLiteCoreTestNet, // base uri
                "QbrPfc6DbHqWcpBmo6XAXEp637pX7MowHk", // address
                "5de6dc17e0d640d4365c29d73e1052979397a968cffa8e42d03736ffe06e6ef5", // output txid
                35318239, // output amount
                "e7cf2102ca8e3c994b075728de8ced8a337bf4a56e1e906f3eef6b2aa0cf5671" // output spent txid
            }
        };

        [Theory]
        [MemberData(nameof(OutputsTestData))]
        public async void GetOutputsTest(
            BitcoinBasedCurrency currency,
            string baseUri,
            string address,
            string outputTxId,
            long outputAmount,
            string outputSpentTxId)
        {
            var api = new InsightApi(currency, baseUri);
            var outputsResult = await api.GetOutputsAsync(address);

            Assert.False(outputsResult.HasError, outputsResult.Error?.Description ?? "");

            var outputs = outputsResult.Value?.ToList();

            Assert.NotNull(outputs);
            Assert.True(outputs.Any());
            Assert.Contains(outputs, o => o.TxId == outputTxId && o.Value == outputAmount &&
                (outputSpentTxId != null && o.SpentTxPoint != null && o.SpentTxPoint.Hash == outputSpentTxId ||
                outputSpentTxId == null && o.SpentTxPoint == null));
        }

        public static IEnumerable<object[]> TransactionTestData => new List<object[]>
        {
            new object[]
            {
                Common.BtcMainNet, // currency
                InsightApi.InsightBitPayMainNet, // base uri
                "d4eb2a63e37391d889c0c8cb9d26bd556ba6721558626af6881049e5b1724b41", // txid
                1, // inputs count
                2, // outputs count
                4775, // fees
                593212 // blockheight
            },
            //new object[]
            //{
            //    Common.BtcTestNet, // currency
            //    InsightApi.InsightBitPayTestNet, // base uri
            //    "5c9cdb6af858512cc5b3c7d9ff1bb45d80ee5f885b06544cefe41681f062f76d", // txid
            //    1, // inputs count
            //    2, // outputs count
            //    5712, // fees
            //    1571477 // blockheight
            //},
            new object[]
            {
                Common.LtcMainNet, // currency
                InsightApi.InsightLiteCoreMainNet, // base uri
                "4764ef25642647206217002f18fecaece0fd5daba38620e5420200567f7ac9de", // txid
                8, // inputs count
                2, // outputs count
                1070400, // fees
                1696548 // blockheight
            },
            new object[]
            {
                Common.LtcTestNet, // currency
                InsightApi.InsightLiteCoreTestNet, // base uri
                "5de6dc17e0d640d4365c29d73e1052979397a968cffa8e42d03736ffe06e6ef5", // txid
                3, // inputs count
                2, // outputs count
                65769, // fees
                1186513 // blockheight
            }
        };

        [Theory]
        [MemberData(nameof(TransactionTestData))]
        public async void GetTransactionTest(
            BitcoinBasedCurrency currency,
            string baseUri,
            string txId,
            int inputsCount,
            int outputsCount,
            long fees,
            int blockHeight)
        {
            var api = new InsightApi(currency, baseUri);
            var txResult = await api.GetTransactionAsync(txId);

            Assert.False(txResult.HasError, txResult.Error?.Description ?? "");

            var tx = txResult.Value as IBitcoinBasedTransaction;

            Assert.NotNull(tx);
            Assert.True(tx.Id == txId);
            Assert.Equal(inputsCount, tx.Inputs.Length);
            Assert.Equal(outputsCount, tx.Outputs.Length);
            Assert.Equal(fees, tx.Fees);
            Assert.Equal(blockHeight, tx.BlockInfo.BlockHeight);
        }

        public static IEnumerable<object[]> IsTransactionOutputSpentTestData => new List<object[]>
        {
            new object[]
            {
                Common.BtcMainNet, // currency
                InsightApi.InsightBitPayMainNet, // base uri
                "d4eb2a63e37391d889c0c8cb9d26bd556ba6721558626af6881049e5b1724b41", // txid
                1, // output no
                "a841db57448bbdd8ffae41d50ac133bfecba1c010fb9e6e1b208b58a363dda2c", // spent tx id
                0, // spent index
            },
            //new object[]
            //{
            //    Common.BtcTestNet, // currency
            //    InsightApi.InsightBitPayTestNet, // base uri
            //    "5c9cdb6af858512cc5b3c7d9ff1bb45d80ee5f885b06544cefe41681f062f76d", // txid
            //    1, // output no
            //    "06cbdcbbccf0d7c77c5c729e7702436f6f3a78f0e4102236f5a911ffcd21526f", // spent tx id
            //    0, // spent index
            //},
            new object[]
            {
                Common.LtcMainNet, // currency
                InsightApi.InsightLiteCoreMainNet, // base uri
                "4764ef25642647206217002f18fecaece0fd5daba38620e5420200567f7ac9de", // txid
                0, // output no
                "ff8edfee3b8213f9941bafa8b617f60d31b4306966b7f0ee40bc963e646801e4", // spent tx id
                0, // spent index
            },
            new object[]
            {
                Common.LtcTestNet, // currency
                InsightApi.InsightLiteCoreTestNet, // base uri
                "5de6dc17e0d640d4365c29d73e1052979397a968cffa8e42d03736ffe06e6ef5", // txid
                1, // output no
                "85ac0a303e5b61e3cbea984a67e5798bc46d728823035428993a2f35f845f8a0", // spent tx id
                0, // spent index
            }
        };

        [Theory]
        [MemberData(nameof(IsTransactionOutputSpentTestData))]
        public async void IsTransactionOutputSpentTest(
            BitcoinBasedCurrency currency,
            string baseUri,
            string txId,
            uint outputNo,
            string spentTxId,
            uint spentIndex)
        {
            var api = new InsightApi(currency, baseUri);
            var spentPointResult = await api.IsTransactionOutputSpent(txId, outputNo);

            Assert.False(spentPointResult.HasError, spentPointResult.Error?.Description ?? "");

            var spentPoint = spentPointResult.Value;

            Assert.NotNull(spentPoint);
            Assert.Equal(spentTxId, spentPoint.Hash);
            Assert.Equal(spentIndex, spentPoint.Index);
        }
    }
}