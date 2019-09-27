using Atomex.Blockchain.Tezos;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class TzScanApiTests
    {
        [Fact]
        public async void GetBalanceAsyncTest()
        {
            var api = new TzScanApi(Common.XtzTestNet, TezosNetwork.Alphanet, Common.XtzTestNet.RpcProvider);

            var balance = await api
                .GetBalanceAsync("tz1LEggDVuvj94YuyjkLDfELDDk3FCF8iA3W")
                .ConfigureAwait(false);
        }

        [Fact]
        public async void GetTransactionAsyncTest()
        {
            var api = new TzScanApi(Common.XtzMainNet, TezosNetwork.Mainnet, Common.XtzMainNet.RpcProvider);

            var tx = await api
                .GetTransactionAsync("oo5fwMjaLq8jzmKH1HJi9Qpg2VAfT3yMsMGtjnbHuCUAWAjiehV")
                .ConfigureAwait(false);

            Assert.NotNull(tx);           
        }

        [Fact]
        public async void GetTransactionsAsyncTest()
        {
            var api = new TzScanApi(Common.XtzTestNet, TezosNetwork.Alphanet, Common.XtzTestNet.RpcProvider);

            var txs = await api
                .GetTransactionsAsync("tz1LEggDVuvj94YuyjkLDfELDDk3FCF8iA3W")
                .ConfigureAwait(false);

            Assert.NotNull(txs);
        }
    }
}