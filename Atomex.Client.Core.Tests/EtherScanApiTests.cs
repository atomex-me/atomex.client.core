using System;
using System.Collections.Generic;
using System.Text;
using Atomex.Blockchain.Ethereum;
using Nethereum.Signer;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class EtherScanApiTests
    {
        [Fact]
        public async void GetBalanceAsyncTest()
        {
            var api = new EtherScanApi(Common.EthTestNet, Chain.Ropsten);

            var balance = await api
                .GetBalanceAsync("0xe4aec93f3c0807b66b3fd043623e21dbbb0a3a82")
                .ConfigureAwait(false);
        }
    }
}