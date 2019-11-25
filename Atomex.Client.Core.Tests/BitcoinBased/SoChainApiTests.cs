//using System;
//using System.Collections.Generic;
//using System.Threading;
//using Atomex.Blockchain.Abstract;
//using Atomex.Blockchain.SoChain;
//using Atomex.Common;
//using Xunit;

//namespace Atomex.Client.Core.Tests
//{
//    public class SoChainApiTests
//    {
//        public static IEnumerable<object[]> BitcoinBasedCurrencies =>
//            new List<object[]>
//            {
//                new object[] {Common.Currencies.Get<Bitcoin>()},
//                new object[] {Common.Currencies.Get<Litecoin>()}
//            };

//        [Theory]
//        [MemberData(nameof(BitcoinBasedCurrencies))]
//        public async void BroadcastAsyncFailTest(BitcoinBasedCurrency currency)
//        {
//            const int total = 6000;
//            const int amount = 3000;
//            const int fee = 1000;

//            var api = new SoChainApi(currency);

//            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, total);

//            var tx = currency.CreatePaymentTx(
//                unspentOutputs: initTx.Outputs,
//                destinationAddress: Common.Bob.PubKey.GetAddress(currency),
//                changeAddress: Common.Alice.PubKey.GetAddress(currency),
//                amount: amount,
//                fee: fee);

//            tx.Sign(Common.Alice, initTx.Outputs);

//            Assert.NotNull(tx);
//            Assert.True(tx.Check());
//            Assert.True(tx.Verify(initTx.Outputs));

//            var txId = await api.BroadcastAsync(tx, CancellationToken.None)
//				.ConfigureAwait(false);

//			Assert.Null(txId);
//        }

//        [Fact]
//        public async void GetInputAsyncTest()
//        {
//            try
//            {
//                var ltc = Common.Currencies.Get<Litecoin>();

//                var input = await ((IInOutBlockchainApi)ltc.BlockchainApi)
//                    .GetInputAsync(
//                        txId: "928bda2d414ca1e2dbebcd8fa3553de50c3bd43be258a8f7c8df63a2911ac8c4",
//                        inputNo: 0)
//                    .ConfigureAwait(false);

//                Assert.NotNull(input);
//            }
//            catch (Exception e)
//            {
//                Assert.True(false, e.Message);
//            }
//        }
//    }
//}