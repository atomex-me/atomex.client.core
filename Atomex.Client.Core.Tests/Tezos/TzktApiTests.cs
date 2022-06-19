using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

using Atomex.Blockchain.Tezos.Tzkt;

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

        //[Fact]
        //public async void CanCheckOperatorActiveAsync()
        //{
        //    var tzktApi = new TzktApi(Common.XtzMainNet);

        //    var isActiveResult = await tzktApi.IsFa2TokenOperatorActiveAsync(
        //        holderAddress: "tz1XFG6C2y45i4rSUAkzjd6G6NshiLqB4oEn",
        //        spenderAddress: "KT1M8asPmVQhFG6yujzttGonznkghocEkbFk",
        //        tokenContractAddress: "KT1XRPEPXbZK25r3Htzp2o1x7xdMMmfocKNW",
        //        tokenId: 0);

        //    Assert.NotNull(isActiveResult);
        //    Assert.False(isActiveResult.HasError);
        //    Assert.True(isActiveResult.Value);
        //}
    }
}