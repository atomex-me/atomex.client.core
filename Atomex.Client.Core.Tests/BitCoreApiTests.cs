using System.Collections.Generic;
using System.Linq;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.BitCore;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class BitCoreApiTests
    {
        public static IEnumerable<object[]> AddressTestData => new List<object[]>
        {
            new object[]
            {
                Common.BtcTestNet, // currency
                BitCoreApi.BitCoreBaseUri, // base uri
                "2MyUqSDvEyhiJmQSWBfyAbdeJyBiDnsFkKn" // address
            },
        };

        [Theory]
        [MemberData(nameof(AddressTestData))]
        public async void GetBalanceTest(
            BitcoinBasedCurrency currency,
            string baseUri,
            string address)
        {
            var api = new BitCoreApi(currency, baseUri);
            var balanceAsyncResult = await api.GetBalanceAsync(address);

            Assert.False(balanceAsyncResult.HasError, balanceAsyncResult.Error?.Description ?? "");
        }

        public static IEnumerable<object[]> TransactionTestData => new List<object[]>
        {
            new object[]
            {
                Common.BtcTestNet, // currency
                BitCoreApi.BitCoreBaseUri, // base uri
                "5c9cdb6af858512cc5b3c7d9ff1bb45d80ee5f885b06544cefe41681f062f76d", // txid
                1, // inputs count
                2, // outputs count
                5712, // fees
                1571477 // blockheight
            },
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
            var api = new BitCoreApi(currency, baseUri);
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

        public static IEnumerable<object[]> InputTestData => new List<object[]>
        {
            new object[]
            {
                Common.BtcTestNet, // currency
                BitCoreApi.BitCoreBaseUri, // base uri
                "5c9cdb6af858512cc5b3c7d9ff1bb45d80ee5f885b06544cefe41681f062f76d", // txid
                0, // input index
                "ce4e2aee1e9789c769ea7110db9f0ab2fc5de3785650ce91f1252fa39bf56088", // prev txid
                1 // prev tx output index
            },
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
            var api = new BitCoreApi(currency, baseUri);
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
            var api = new BitCoreApi(currency, baseUri);
            var utxoResult = await api.GetUnspentOutputsAsync(address);

            Assert.False(utxoResult.HasError, utxoResult.Error?.Description ?? "");

            var utxo = utxoResult.Value;

            Assert.NotNull(utxo);
        }

        public static IEnumerable<object[]> OutputsTestData => new List<object[]>
        {
            new object[]
            {
                Common.BtcTestNet, // currency
                BitCoreApi.BitCoreBaseUri, // base uri
                "2MyUqSDvEyhiJmQSWBfyAbdeJyBiDnsFkKn", // address
                "8329821b8c77eff08b928b02a6d83d0102b615cf7c2d0a89a96f0a7647f6a3d9", // output txid
                1098605, // output amount
                "1e606a544785665544f0ad1bdcc0047d3dfdb0e6006729d0138cfe072781358a" // output spent txid
            },
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
            var api = new BitCoreApi(currency, baseUri);
            var outputsResult = await api.GetOutputsAsync(address);

            Assert.False(outputsResult.HasError, outputsResult.Error?.Description ?? "");

            var outputs = outputsResult.Value?.ToList();

            Assert.NotNull(outputs);
            Assert.True(outputs.Any());
            Assert.Contains(outputs, o => o.TxId == outputTxId && o.Value == outputAmount &&
                (outputSpentTxId != null && o.SpentTxPoint != null && o.SpentTxPoint.Hash == outputSpentTxId ||
                outputSpentTxId == null && o.SpentTxPoint == null));
        }

        public static IEnumerable<object[]> IsTransactionOutputSpentTestData => new List<object[]>
        {
            new object[]
            {
                Common.BtcTestNet, // currency
                BitCoreApi.BitCoreBaseUri, // base uri
                "5c9cdb6af858512cc5b3c7d9ff1bb45d80ee5f885b06544cefe41681f062f76d", // txid
                1, // output no
                "06cbdcbbccf0d7c77c5c729e7702436f6f3a78f0e4102236f5a911ffcd21526f", // spent tx id
                0, // spent index
            },
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
            var api = new BitCoreApi(currency, baseUri);
            var spentPointResult = await api.IsTransactionOutputSpent(txId, outputNo);

            Assert.False(spentPointResult.HasError, spentPointResult.Error?.Description ?? "");

            var spentPoint = spentPointResult.Value;

            Assert.NotNull(spentPoint);
            Assert.Equal(spentTxId, spentPoint.Hash);
            Assert.Equal(spentIndex, spentPoint.Index);
        }
    }
}