using Atomex.Blockchain.Tezos;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class TzScanApiTests
    {
        [Fact]
        public async void GetBalanceAsyncTest()
        {
            var api = new TzScanApi(Common.XtzTestNet);

            var balanceAsyncResult = await api
                .GetBalanceAsync("tz1LEggDVuvj94YuyjkLDfELDDk3FCF8iA3W")
                .ConfigureAwait(false);

            Assert.False(balanceAsyncResult.HasError);
        }

        [Fact]
        public async void GetTransactionAsyncTest()
        {
            var api = new TzScanApi(Common.XtzMainNet);

            var txAsyncResult = await api
                .GetTransactionAsync("oo5fwMjaLq8jzmKH1HJi9Qpg2VAfT3yMsMGtjnbHuCUAWAjiehV")
                .ConfigureAwait(false);

            Assert.False(txAsyncResult.HasError);

            var tx = txAsyncResult.Value;

            Assert.NotNull(tx);           
        }

        [Fact]
        public async void GetTransactionsAsyncTest()
        {
            var api = new TzScanApi(Common.XtzTestNet);

            var txsAsyncResult = await api
                .GetTransactionsAsync("tz1LEggDVuvj94YuyjkLDfELDDk3FCF8iA3W")
                .ConfigureAwait(false);

            Assert.False(txsAsyncResult.HasError);

            var txs = txsAsyncResult.Value;

            Assert.NotNull(txs);
        }
    }
}