using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

using Atomex.Blockchain.Tezos;

namespace Atomex.Client.Core.Tests
{
    public class TzktApiTests
    {
        [Fact]
        public async void CanGetTransactionAsync()
        {
            var tzktApi = new TzktApi(Common.XtzMainNet);

            var tx = await tzktApi.GetTransactionAsync("ooq2ZsLwonxAhbZKyRNtodJkUXtxKpkohjx1wt7MStPB5ksE7yw");

            Assert.NotNull(tx);
        }
    }
}