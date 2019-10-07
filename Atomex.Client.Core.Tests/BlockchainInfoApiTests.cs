using System.Collections.Generic;
using Atomex.Blockchain.BlockchainInfo;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class BlockchainInfoApiTests
    {
        public static IEnumerable<object[]> GetTransactionTestData => new List<object[]>
        {
            //new object[] {Common.TestNetBtc, "aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8"},
            new object[] {Common.BtcMainNet, "2ba30a70e5b0bfc0186cd6fe53d7a817a3072cad26392f38aaca0ffacd790a68"}
        };

        [Theory]
        [MemberData(nameof(GetTransactionTestData))]
        public async void GetTransactionTest(BitcoinBasedCurrency currency, string txId)
        {
            var api = new BlockchainInfoApi(currency);
            var asyncResult = await api.GetTransactionAsync(txId);
            var tx = asyncResult.Value;

            Assert.False(asyncResult.HasError);
            Assert.NotNull(tx);
            Assert.True(tx.Id == txId);
        }

        public static IEnumerable<object[]> InputTestData => new List<object[]>
        {
            //new object[] {Common.TestNetBtc, "aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8", 0},
            new object[] {Common.BtcMainNet, "2ba30a70e5b0bfc0186cd6fe53d7a817a3072cad26392f38aaca0ffacd790a68", 0}
        };

        [Theory]
        [MemberData(nameof(InputTestData))]
        public async void GetInputTest(BitcoinBasedCurrency currency, string txId, uint inputNo)
        {
            var api = new BlockchainInfoApi(currency);
            var asyncResult = await api.GetInputAsync(txId, inputNo);
            var input = asyncResult.Value;

            Assert.False(asyncResult.HasError);
            Assert.NotNull(input);
        }

        //[Fact]
        //public async void GetTransactionTest()
        //{
        //    //var soChain = new SoChainApi(Common.Btc);
        //    var bcInfo = new BlockchainInfoApi(Common.Btc);
        //    var soTx = await soChain.GetTransactionAsync("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8");
        //    var bcTx = await bcInfo.GetTransactionAsync("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8");

        //    Assert.True(soTx.Id == bcTx.Id);
        //    Assert.True(soTx.Currency.Name == bcTx.Currency.Name);
        //    Assert.True(soTx.BlockInfo.BlockHeight == bcTx.BlockInfo.BlockHeight);
        //    Assert.True(soTx.BlockInfo.Fees == bcTx.BlockInfo.Fees);
        //    //Assert.True(soTx.BlockInfo.Confirmations == bcTx.BlockInfo.Confirmations);
        //}

        //[Fact]
        //public async Task GetInputsTest()
        //{
        //    //var soChain = new SoChainApi(Common.Btc);
        //    var bcInfo = new BlockchainInfoApi(Common.Btc);

        //    var soIn = (await soChain.GetInputsAsync("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8")).ToList()[0];
        //    var bcIn = (await bcInfo.GetInputsAsync("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8")).ToList()[0];

        //    Assert.True(soIn.Hash == bcIn.Hash);
        //    Assert.True(soIn.Index == bcIn.Index);
        //}

        //[Fact]
        //public async void GetInputTest()
        //{
        //    //var soChain = new SoChainApi(Common.Btc);
        //    var bcInfo = new BlockchainInfoApi(Common.Btc);

        //    var soIn = await soChain.GetInputAsync("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8", 0);
        //    var bcIn = await bcInfo.GetInputAsync("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8", 0);

        //    Assert.True(soIn.Hash == bcIn.Hash);
        //    Assert.True(soIn.Index == bcIn.Index);
        //}

        //[Fact]
        //public async void GetOutputsTest()
        //{
        //    //var soChain = new SoChainApi(Common.Btc);
        //    var bcInfo = new BlockchainInfoApi(Common.Btc);

        //    var bcOuts = await bcInfo.GetOutputsAsync("2NBgXvWR9DRZaJzkpHXa134txembCSW8S4h");
        //    var soOuts = await soChain.GetOutputsAsync("2NBgXvWR9DRZaJzkpHXa134txembCSW8S4h");

        //    if (!soOuts.Any())
        //    {
        //        return;
        //    }

        //    var soOut = soOuts.FirstOrDefault();
        //    var bcOut = bcOuts.FirstOrDefault(x => x.TxId == soOut.TxId);
        //    Assert.True(soOut.Index == bcOut.Index, "1");
        //    Assert.True(soOut.TxId == bcOut.TxId, "2");
        //    Assert.True(soOut.IsSpent == bcOut.IsSpent, "3");
        //    Assert.True(soOut.IsValid == bcOut.IsValid, "4");
        //    Assert.True(soOut.IsSwapPayment == bcOut.IsSwapPayment, "5");
        //    Assert.True(soOut.Value == bcOut.Value, "6");
        //    Assert.True(soOut.SpentTxPoint.Hash == bcOut.SpentTxPoint.Hash, "7");
        //    Assert.True(soOut.SpentTxPoint.Index == bcOut.SpentTxPoint.Index, "8");
        //}

        //[Fact]
        //public async void GetUnspentOutputTest()
        //{
        //    //var soChain = new SoChainApi(Common.Btc);
        //    var bcInfo = new BlockchainInfoApi(Common.Btc);

        //    var soOuts = (await soChain.GetUnspentOutputsAsync("2NBgXvWR9DRZaJzkpHXa134txembCSW8S4h"));
        //    var bcOuts = (await bcInfo.GetUnspentOutputsAsync("2NBgXvWR9DRZaJzkpHXa134txembCSW8S4h"));

        //    if (!soOuts.Any())
        //    {
        //        return;
        //    }

        //    var soIn = soOuts.FirstOrDefault();
        //    var bcIn = bcOuts.FirstOrDefault(x => x.TxId == soIn.TxId);

        //    Assert.True(soIn.Value == bcIn.Value);
        //    Assert.True(soIn.Index == bcIn.Index);
        //    Assert.True(soIn.TxId == bcIn.TxId);
        //    Assert.True(soIn.IsSpent == bcIn.IsSpent);
        //    Assert.True(soIn.IsValid == bcIn.IsValid);
        //    Assert.True(soIn.IsSwapPayment == bcIn.IsSwapPayment);
        //    Assert.True(soIn.SpentTxPoint.Hash == bcIn.SpentTxPoint.Hash);
        //    Assert.True(soIn.SpentTxPoint.Index == bcIn.SpentTxPoint.Index);
        //}

        //[Fact]
        //public async void IsTransactionSpentTest()
        //{
        //    var soChain = new SoChainApi(Common.Btc);
        //    var bcInfo = new BlockchainInfoApi(Common.Btc);

        //    var soOuts = (await soChain.IsTransactionOutputSpent("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8", 0));
        //    var bcOuts = (await bcInfo.IsTransactionOutputSpent("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8", 0));
        //    if (soOuts == null)
        //    {
        //        return;
        //    }
        //    Assert.True(soOuts.Hash == bcOuts.Hash);
        //    Assert.True(soOuts.Index == bcOuts.Index);
        //}
    }
}