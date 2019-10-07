using System.Collections.Generic;
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
                Common.BtcMainNet, // currency
                BitCoreApi.BitCoreBaseUri, // base uri
                "17c5d61XW7714Abk7yNxKZkRybiCR9ft6m" // address
            },
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

            Assert.False(balanceAsyncResult.HasError);
        }

        public static IEnumerable<object[]> TransactionTestData => new List<object[]>
        {
            new object[]
            {
                Common.BtcMainNet, // currency
                BitCoreApi.BitCoreBaseUri, // base uri
                "d4eb2a63e37391d889c0c8cb9d26bd556ba6721558626af6881049e5b1724b41", // txid
                1, // inputs count
                2, // outputs count
                4775, // fees
                593212 // blockheight
            },
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
            //var api = new BitCoreApi(currency, baseUri);
            //var txAsyncResult = await api.GetTransactionAsync(txId);

            //Assert.False(txAsyncResult.HasError);

            //var tx = txAsyncResult.Value as IBitcoinBasedTransaction;

            //Assert.NotNull(tx);
            //Assert.True(tx.Id == txId);
            //Assert.Equal(inputsCount, tx.Inputs.Length);
            //Assert.Equal(outputsCount, tx.Outputs.Length);
            //Assert.Equal(fees, tx.Fees);
            //Assert.Equal(blockHeight, tx.BlockInfo.BlockHeight);
        }
    }
}