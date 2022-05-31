using System.Linq;

using Xunit;

using Atomex.Blockchain.Tezos.Tzkt.Swaps.V1;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Common;
using Atomex.Cryptography.Abstract;

namespace Atomex.Blockchain.Tezos
{

    public class TzktSwapHelperTests
    {
        private TzktApi CreateApi() => new TzktApi(new TzktSettings { BaseUri = TzktApi.Uri });

        [Theory]
        [InlineData(
            "b53fa104ff20439c20c81e281b0dbc332d728d4e8ea4e8063699ff2964e69790",
            "KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL",
            "tz1fyFNErtQnHZz6WujUTKzv4dZsqEggAW4P",
            1568384514,
            32400,
            "ooq2ZsLwonxAhbZKyRNtodJkUXtxKpkohjx1wt7MStPB5ksE7yw")]
        [InlineData(
            "3d714832e754376141795c06ad9dd90722f2410a1fcbdc5f142f21d1234d30d4",
            "KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL",
            "tz1QB5LdNbZgfC2zxa26bMLARwDjv63M9gCg",
            1569231343,
            36000,
            "oovqYwZvCjwHzW4pGHaxqqvTz4nJFbDKbuetZNGkgHkzmp92Ban")]
        [InlineData(
            "c3819615e23264eaff2c4d343ba9e524499a7d9e504a8542887b08337fd4c4d2",
            "KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL",
            "tz1fyFNErtQnHZz6WujUTKzv4dZsqEggAW4P",
            1569924103,
            36000,
            "onzLoJkMzPD6BEoJ7YLH7ts6ZodGJhDTmdoSvLRCWwNLdL4rcMb")]
        [HasNetworkRequests()]
        public async void CanFindLockTxsAsync(
            string secretHash,
            string contractAddress,
            string address,
            ulong timeStamp,
            ulong lockTime,
            string txId)
        {
            var (txs, error) = await TzktSwapHelper.FindLocksAsync(
                api: CreateApi(),
                secretHash: secretHash,
                contractAddress: contractAddress,
                address: address,
                timeStamp: timeStamp,
                lockTime: lockTime);

            Assert.Null(error);
            Assert.NotNull(txs);
            Assert.NotEmpty(txs);
            Assert.Contains(txs, o => o.TxId == txId);
        }

        [Theory]
        [InlineData(
            "b84cdbd752d0a33e36b30809b3a5261477dfd657b283b48d7f285190f8cfa6bb",
            "KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL",
            1569231343,
            1,
            "oohAKdeqCEpsqNWv1Dpaz7rQkqai4RL2JospKEAXq44tZ7fgYe2",
            null,
            null,
            null)]
        [InlineData(
            "5d9c771ddad28e97ff70e6cbdb11ebba4c91eac2c38e67eaa5af205dd126d75e",
            "KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL",
            1572447913,
            4,
            "onuBuxuy5Kfv8Erg1bzBVLGwYLw81TdjwMoLwZJW8cjcakChsu3",
            "ooSeeJzgm9ReivyVBp6ZUVqG3jLa2E9Y7XqdJNpRUUiLpMGKzWy",
            "op5g4sEWmR9Pt2Po5nX25Ua36R4JEmHtM8B18toUpHXJvMtGTHZ",
            "ooHzFooYMq6G6STRYtVqaCE9yu18qAF82REWgQy9AN8SHBmwg9r")]
        [HasNetworkRequests()]
        public async void CanFindAddTxsAsync(
            string secretHash,
            string contractAddress,
            ulong timeStamp,
            int calls,
            string txId1,
            string txId2,
            string txId3,
            string txId4)
        {
            var (txs, error) = await TzktSwapHelper.FindAdditionalLocksAsync(
                api: CreateApi(),
                secretHash: secretHash,
                contractAddress: contractAddress,
                timeStamp: timeStamp);

            Assert.Null(error);
            Assert.NotNull(txs);
            Assert.NotEmpty(txs);
            Assert.True(txs.Count() >= calls);
            Assert.Contains(txs, o => o.TxId == txId1);

            if (txId2 != null)
                Assert.Contains(txs, o => o.TxId == txId2);

            if (txId3 != null)
                Assert.Contains(txs, o => o.TxId == txId3);

            if (txId4 != null)
                Assert.Contains(txs, o => o.TxId == txId4);
        }

        [Theory]
        [InlineData(
            "3ad907958d841796d1bfd9b5070c4254944d21e768a7a17a9fdc03fba9627cd3",
            "KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL",
            1569231343,
            "opBZpJuqYpiYNHjHueKHJsnsNBKVzvbhyE6LXX7P2tFgqRfbacA")]
        [InlineData(
            "fff4fe0d4a1c21984353f882ed418a2849b1ebaaf6b591dae6c3cca002b47401",
            "KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL",
            1569924103,
            "oo2mGsTfViTBuCqrVtG6xB55bNSWSVLR3thejyfMhy1BGnDCgWk")]
        [InlineData(
            "b99cc5c746bc0cc53cc8a30cb0b6e3375bc0282945daf0557768818ddcba1778",
            "KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL",
            1572254088,
            "opDmtHvk8gbjxB3WLuUGehENAgGzFEvoDnPUDZGCC8rQeaqkc6P")]
        [HasNetworkRequests()]
        public async void CanFindRedeemTxsAsync(
            string secret,
            string contractAddress,
            ulong timeStamp,
            string txId)
        {
            var (txs, error) = await TzktSwapHelper.FindRedeemsAsync(
                api: CreateApi(),
                secretHash: HashAlgorithm.Sha256
                    .Hash(Hex.FromString(secret), iterations: 2)
                    .ToHexString(),
                contractAddress: contractAddress,
                timeStamp: timeStamp);

            Assert.Null(error);
            Assert.NotNull(txs);
            Assert.NotEmpty(txs);
            Assert.Contains(txs, o => o.TxId == txId);
        }

        [Theory]
        [InlineData(
            "b53fa104ff20439c20c81e281b0dbc332d728d4e8ea4e8063699ff2964e69790",
            "KT1VG2WtYdSWz5E7chTeAdDPZNy2MpP8pTfL",
            1568384514,
            "onfcz6MFgoqvhC465UtLrXuuLj7osdXqeyH1w6pbpP42GGVxH2V")]
        [HasNetworkRequests()]
        public async void CanFindRefundTxsAsync(
            string secretHash,
            string contractAddress,
            ulong timeStamp,
            string txId)
        {
            var (txs, error) = await TzktSwapHelper.FindRefundsAsync(
                api: CreateApi(),
                secretHash: secretHash,
                contractAddress: contractAddress,
                timeStamp: timeStamp);

            Assert.Null(error);
            Assert.NotNull(txs);
            Assert.NotEmpty(txs);
            Assert.Contains(txs, o => o.TxId == txId);
        }
    }
}