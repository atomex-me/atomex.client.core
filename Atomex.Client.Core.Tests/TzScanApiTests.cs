using Atomex.Blockchain.Tezos;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class TzScanApiTests
    {
        [Fact]
        public async void GetBalanceAsyncTest()
        {
            var api = new TzScanApi(Common.XtzMainNet);

            var balanceResult = await api
                .GetBalanceAsync("tz1emGQ4PFNUprQv5jfKF3GyrDowPRBvtwnX")
                .ConfigureAwait(false);

            Assert.False(balanceResult.HasError, balanceResult.Error?.Description ?? "");
        }

        [Fact]
        public async void GetTransactionAsyncTest()
        {
            var api = new TzScanApi(Common.XtzMainNet);

            var txResult = await api
                .GetTransactionAsync("oo5fwMjaLq8jzmKH1HJi9Qpg2VAfT3yMsMGtjnbHuCUAWAjiehV")
                .ConfigureAwait(false);

            Assert.False(txResult.HasError, txResult.Error?.Description ?? "");

            var tx = txResult.Value;

            Assert.NotNull(tx);           
        }

        [Fact]
        public async void GetTransactionsAsyncTest()
        {
            var api = new TzScanApi(Common.XtzMainNet);

            var txsResult = await api
                .GetTransactionsAsync("tz1emGQ4PFNUprQv5jfKF3GyrDowPRBvtwnX")
                .ConfigureAwait(false);

            Assert.False(txsResult.HasError);

            var txs = txsResult.Value;

            Assert.NotNull(txs);
        }
    }
}