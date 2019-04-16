using System.Linq;
using Atomix.Blockchain.BlockchainInfo;
using Atomix.Blockchain.SoChain;
using Xunit;
using Xunit.Abstractions;

namespace Atomix.Client.Core.Tests
{
    public class BlockchainInfoApiTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public BlockchainInfoApiTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public async void GetTransactionTest()
        {
            var soChain = new SoChainApi(currency: new Bitcoin());
            var bcInfo = new BlockchainInfoApi();
            var soTx = await soChain.GetTransactionAsync("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8");
            var bcTx = await bcInfo.GetTransactionAsync("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8");
            
            Assert.True(soTx.Id == bcTx.Id);
            Assert.True(soTx.Currency.Name == bcTx.Currency.Name);
            Assert.True(soTx.BlockInfo.BlockHeight == bcTx.BlockInfo.BlockHeight);
            Assert.True(soTx.BlockInfo.Fees == bcTx.BlockInfo.Fees);
            //Assert.True(soTx.BlockInfo.Confirmations == bcTx.BlockInfo.Confirmations);
        }
        
        [Fact]
        public async void GetInputsTest()
        {
            var soChain = new SoChainApi(currency: new Bitcoin());
            var bcInfo = new BlockchainInfoApi();

            var soIn = (await soChain.GetInputsAsync("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8")).ToList()[0];
            var bcIn = (await bcInfo.GetInputsAsync("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8")).ToList()[0];
            
            Assert.True(soIn.Hash == bcIn.Hash);
            Assert.True(soIn.Index == bcIn.Index);
        }
        
        [Fact]
        public async void GetInputTest()
        {
            var soChain = new SoChainApi(currency: new Bitcoin());
            var bcInfo = new BlockchainInfoApi();

            var soIn = await soChain.GetInputAsync("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8", 0);
            var bcIn = await bcInfo.GetInputAsync("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8", 0);
            
            Assert.True(soIn.Hash == bcIn.Hash);
            Assert.True(soIn.Index == bcIn.Index);
        }

        [Fact]
        public async void GetOutputsTest()
        {
            var soChain = new SoChainApi(currency: new Bitcoin());
            var bcInfo = new BlockchainInfoApi();
            
            var bcOuts = await bcInfo.GetOutputsAsync("2NBgXvWR9DRZaJzkpHXa134txembCSW8S4h");
            var soOuts = await soChain.GetOutputsAsync("2NBgXvWR9DRZaJzkpHXa134txembCSW8S4h");

            if (!soOuts.Any())
            {
                return;
            }

            var soOut = soOuts.FirstOrDefault();
            var bcOut = bcOuts.FirstOrDefault(x => x.TxId == soOut.TxId);
            Assert.True(soOut.Index == bcOut.Index, "1");
            Assert.True(soOut.TxId == bcOut.TxId, "2");
            Assert.True(soOut.IsSpent == bcOut.IsSpent, "3");
            Assert.True(soOut.IsValid == bcOut.IsValid, "4");
            Assert.True(soOut.IsSwapPayment == bcOut.IsSwapPayment, "5");
            Assert.True(soOut.Value == bcOut.Value, "6");
            Assert.True(soOut.SpentTxPoint.Hash == bcOut.SpentTxPoint.Hash, "7");
            Assert.True(soOut.SpentTxPoint.Index == bcOut.SpentTxPoint.Index, "8");
        }

        [Fact]
        public async void GetUnspentOutputTest()
        {
            var soChain = new SoChainApi(currency: new Bitcoin());
            var bcInfo = new BlockchainInfoApi();

            var soOuts = (await soChain.GetUnspentOutputsAsync("2NBgXvWR9DRZaJzkpHXa134txembCSW8S4h"));
            var bcOuts = (await bcInfo.GetUnspentOutputsAsync("2NBgXvWR9DRZaJzkpHXa134txembCSW8S4h"));
            
            if (!soOuts.Any())
            {
                return;
            }
            
            var soIn = soOuts.FirstOrDefault();
            var bcIn = bcOuts.FirstOrDefault(x => x.TxId == soIn.TxId);
            
            Assert.True(soIn.Value == bcIn.Value);
            Assert.True(soIn.Index == bcIn.Index);
            Assert.True(soIn.TxId == bcIn.TxId);
            Assert.True(soIn.IsSpent == bcIn.IsSpent);
            Assert.True(soIn.IsValid == bcIn.IsValid);
            Assert.True(soIn.IsSwapPayment == bcIn.IsSwapPayment);
            Assert.True(soIn.SpentTxPoint.Hash == bcIn.SpentTxPoint.Hash);
            Assert.True(soIn.SpentTxPoint.Index == bcIn.SpentTxPoint.Index);
        }

        [Fact]
        public async void IsTransactionSpentTest()
        {
            var soChain = new SoChainApi(currency: new Bitcoin());
            var bcInfo = new BlockchainInfoApi();

            var soOuts = (await soChain.IsTransactionOutputSpent("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8", 0));
            var bcOuts = (await bcInfo.IsTransactionOutputSpent("aa1b99c63f2a28dd4d0e1765194d9810cc937ecb9a25f0ca04a485e0dd433ca8", 0));
            if (soOuts == null)
            {
                return;
            }
            Assert.True(soOuts.Hash == bcOuts.Hash);
            Assert.True(soOuts.Index == bcOuts.Index);
        }
    }
}