using System.Linq;
using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class EtherScanApiTests
    {
        [Fact]
        public async void GetBalanceAsyncTest()
        {
            var api = new EtherScanApi(Common.EthTestNet);

            var asyncResult = await api
                .GetBalanceAsync("0xe4aec93f3c0807b66b3fd043623e21dbbb0a3a82")
                .ConfigureAwait(false);

            Assert.False(asyncResult.HasError);
        }

        [Fact]
        public async void GetInitiatedEventTest()
        {
            var api = new EtherScanApi(Common.EthTestNet);

            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<InitiatedEventDTO>();

            var eventsAsyncResult = await api.GetContractEventsAsync(
                    address: "0x527d1049837edf5f99c287a41a87702686082bf8",
                    fromBlock: Common.EthTestNet.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: eventSignatureHash,
                    topic1: "0x87639bcb4d5e61e52398acb13181ddec825744f8fd90a3f8efa68c129a968d0f")
                .ConfigureAwait(false);

            Assert.False(eventsAsyncResult.HasError);

            var events = eventsAsyncResult.Value?.ToList();

            Assert.NotNull(events);
            Assert.Single(events);
            Assert.True(events.First().IsInitiatedEvent());
        
            var initiatedEvent = events.First().ParseInitiatedEvent();
            Assert.NotNull(initiatedEvent);
            Assert.Equal("87639bcb4d5e61e52398acb13181ddec825744f8fd90a3f8efa68c129a968d0f", initiatedEvent.HashedSecret.ToHexString());
            Assert.Equal("0xcd38a31acf4db224a20052acb1934c993680b3e2", initiatedEvent.Participant);
            Assert.Equal("0x981c7251a11a1614d4c70c0f3507bbda54808065", initiatedEvent.Initiator);
            Assert.Equal(1569198635, (long)initiatedEvent.RefundTimestamp);
            Assert.Equal(27645001080510000, (long)initiatedEvent.Value);
            Assert.Equal(0, (long)initiatedEvent.RedeemFee);
        }

        [Fact]
        public async void GetAddedEventTest()
        {
            var api = new EtherScanApi(Common.EthTestNet);

            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<AddedEventDTO>();

            var eventsAsyncResult = await api.GetContractEventsAsync(
                    address: "0x527d1049837edf5f99c287a41a87702686082bf8",
                    fromBlock: Common.EthTestNet.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: eventSignatureHash,
                    topic1: "0xbe51acca480dba043159355d597e39744ad7140d325f6cb3c1554db6b33947d6")
                .ConfigureAwait(false);

            Assert.False(eventsAsyncResult.HasError);

            var events = eventsAsyncResult.Value?.ToList();

            Assert.NotNull(events);
            Assert.Single(events);
            Assert.True(events.First().IsAddedEvent());

            var addedEvent = events.First().ParseAddedEvent();
            Assert.NotNull(addedEvent);
            Assert.Equal("be51acca480dba043159355d597e39744ad7140d325f6cb3c1554db6b33947d6", addedEvent.HashedSecret.ToHexString());
            Assert.Equal("0xe4aec93f3c0807b66b3fd043623e21dbbb0a3a82", addedEvent.Initiator);
            Assert.Equal(289637150000000000, (long)addedEvent.Value);
        }

        [Fact]
        public async void GetRedeemedEventTest()
        {
            var api = new EtherScanApi(Common.EthTestNet);

            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<RedeemedEventDTO>();

            var eventsAsyncResult = await api.GetContractEventsAsync(
                    address: "0x527d1049837edf5f99c287a41a87702686082bf8",
                    fromBlock: Common.EthTestNet.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: eventSignatureHash,
                    topic1: "0x7ca4344b5d8e624917b6b0cee015bab65397349062ec2fcdbaebc25d5e1cbb4d")
                .ConfigureAwait(false);

            Assert.False(eventsAsyncResult.HasError);

            var events = eventsAsyncResult.Value?.ToList();

            Assert.NotNull(events);
            Assert.Single(events);
            Assert.True(events.First().IsRedeemedEvent());

            var redeemedEvent = events.First().ParseRedeemedEvent();
            Assert.NotNull(redeemedEvent);
            Assert.Equal("7ca4344b5d8e624917b6b0cee015bab65397349062ec2fcdbaebc25d5e1cbb4d", redeemedEvent.HashedSecret.ToHexString());
            Assert.Equal("f53c77f812c16243d3cdffc48fc4fd5a1f36541db9409b112a0e56436fc7fa35", redeemedEvent.Secret.ToHexString());
        }

        [Fact]
        public async void GetRefundedEventTest()
        {
            var api = new EtherScanApi(Common.EthTestNet);

            var eventSignatureHash = EventSignatureExtractor.GetSignatureHash<RefundedEventDTO>();

            var eventsAsyncResult = await api.GetContractEventsAsync(
                    address: "0x527d1049837edf5f99c287a41a87702686082bf8",
                    fromBlock: Common.EthTestNet.SwapContractBlockNumber,
                    toBlock: ulong.MaxValue,
                    topic0: eventSignatureHash,
                    topic1: "0xbe51acca480dba043159355d597e39744ad7140d325f6cb3c1554db6b33947d6")
                .ConfigureAwait(false);
            
            Assert.False(eventsAsyncResult.HasError);

            var events = eventsAsyncResult.Value?.ToList();

            Assert.NotNull(events);
            Assert.Single(events);
            Assert.True(events.First().IsRefundedEvent());

            var refundedEvent = events.First().ParseRefundedEvent();
            Assert.NotNull(refundedEvent);
            Assert.Equal("be51acca480dba043159355d597e39744ad7140d325f6cb3c1554db6b33947d6", refundedEvent.HashedSecret.ToHexString());
        }
    }
}