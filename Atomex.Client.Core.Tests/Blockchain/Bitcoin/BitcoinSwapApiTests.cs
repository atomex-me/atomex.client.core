using System;
using System.Collections.Generic;
using System.Linq;

using NBitcoin;
using Xunit;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Bitcoin
{
    public abstract class BitcoinSwapApiTests
    {
        public class BitcoinSwapTransactionTestData
        {
            public string Network { get; set; }
            public string Currency { get; set; }
            public string TxId { get; set; }
            public string SecretHash { get; set; }
            public string Address { get; set; }
            public string RefundAddress { get; set; }
            public ulong TimeStamp { get; set; }
            public ulong LockTime { get; set; }
            public int SecretSize { get; set; }
        }

        public static IEnumerable<object[]> LockTransactionData = new List<object[]>
        {
            new object[]
            {
                new BitcoinSwapTransactionTestData
                {
                    Network       = "BTC",
                    Currency      = "BTC",
                    TxId          = "19379d4e1a5d3bbb9f266c1481465a5d9ce7a5b6b9345235cee0e4f358b25b7f",
                    SecretHash    = "48fa6306876c3e3522514fa2e59ca044250bb23fe8cdccf2d885ed27b2155bef",
                    Address       = "1GcbdttpPYEgGWpFjMYfN9RHM88NjQ9Ygo",
                    RefundAddress = "1KeyxnydRq4ttPhvWbscK4TTAToSrSbasJ",
                    TimeStamp     = (ulong) DateTimeOffset
                        .Parse("2021-04-21T10:49:52.971849Z")
                        .ToUnixTimeSeconds(),
                    LockTime      = 36000,
                    SecretSize    = 32
                }
            },
            new object[]
            {
                new BitcoinSwapTransactionTestData
                {
                    Network       = "BTC",
                    Currency      = "BTC",
                    TxId          = "b46242070080ada8642432f7fdf53359fd480103564694c71f8b40f6ac388e9d",
                    SecretHash    = "f11320fa86de37de43255797dc6a882ee571fd602f87a5051238846c3e49dd81",
                    Address       = "1EWULmQQoL3sXqQNiUvED6Xq8SHu2TC8yP",
                    RefundAddress = "13mEgbt1WrxbfCp8nSSnrvxfVx4qXqDvTL",
                    TimeStamp     = (ulong) DateTimeOffset
                        .Parse("2021-04-17T00:41:38.287238Z")
                        .ToUnixTimeSeconds(),
                    LockTime      = 36000,
                    SecretSize    = 32
                }
            },
        };

        public static IEnumerable<object[]> RedeemTransactionData = new List<object[]>
        {
            new object[]
            {
                new BitcoinSwapTransactionTestData
                {
                    Network       = "BTC",
                    Currency      = "BTC",
                    TxId          = "a3f3a7ca5f68109b1a6231f9a5e9cf2efe55b80bb5de90b25f187f62aff7802b",
                    SecretHash    = "48fa6306876c3e3522514fa2e59ca044250bb23fe8cdccf2d885ed27b2155bef",
                    Address       = "1GcbdttpPYEgGWpFjMYfN9RHM88NjQ9Ygo",
                    RefundAddress = "1KeyxnydRq4ttPhvWbscK4TTAToSrSbasJ",
                    TimeStamp     = (ulong) DateTimeOffset
                        .Parse("2021-04-21T10:49:52.971849Z")
                        .ToUnixTimeSeconds(),
                    LockTime      = 36000,
                    SecretSize    = 32
                }
            },
        };

        public static IEnumerable<object[]> RefundTransactionData = new List<object[]>
        {
            new object[]
            {
                new BitcoinSwapTransactionTestData
                {
                    Network       = "BTC",
                    Currency      = "BTC",
                    TxId          = "00aca10aa0908c18a126a6a7c8249f727e5f542c1220b2a4e451c0e25c34e3d3",
                    SecretHash    = "f11320fa86de37de43255797dc6a882ee571fd602f87a5051238846c3e49dd81",
                    Address       = "1EWULmQQoL3sXqQNiUvED6Xq8SHu2TC8yP",
                    RefundAddress = "13mEgbt1WrxbfCp8nSSnrvxfVx4qXqDvTL",
                    TimeStamp     = (ulong) DateTimeOffset
                        .Parse("2021-04-17T00:41:38.287238Z")
                        .ToUnixTimeSeconds(),
                    LockTime      = 36000,
                    SecretSize    = 32
                }
            },
        };

        public abstract IBlockchainSwapApi CreateApi(string currency, string network);
        public abstract Network ResolveNetwork(string currency, string network);

        [Theory]
        [MemberData(nameof(LockTransactionData))]
        [HasNetworkRequests()]
        public async void CanFindLockTransactions(
            BitcoinSwapTransactionTestData testData)
        {
            var api = CreateApi(testData.Currency, testData.Network);
            var network = ResolveNetwork(testData.Currency, testData.Network);

            var (txs, error) = await api.FindLocksAsync(
                secretHash: testData.SecretHash,
                contractAddress: null,
                address: testData.Address,
                refundAddress: testData.RefundAddress,
                timeStamp: testData.TimeStamp,
                lockTime: testData.LockTime,
                secretSize: testData.SecretSize);

            Assert.NotNull(txs);
            Assert.Null(error);

            Assert.True(txs.Count() >= 1);
            Assert.Contains(txs, t => t.TxId == testData.TxId);
        }

        [Theory]
        [MemberData(nameof(RedeemTransactionData))]
        [HasNetworkRequests()]
        public async void CanFindRedeemTransactions(
            BitcoinSwapTransactionTestData testData)
        {
            var api = CreateApi(testData.Currency, testData.Network);
            var network = ResolveNetwork(testData.Currency, testData.Network);

            var (txs, error) = await api.FindRedeemsAsync(
                secretHash: testData.SecretHash,
                contractAddress: null,
                address: testData.Address,
                refundAddress: testData.RefundAddress,
                timeStamp: testData.TimeStamp,
                lockTime: testData.LockTime,
                secretSize: testData.SecretSize);

            Assert.NotNull(txs);
            Assert.Null(error);

            Assert.True(txs.Count() >= 1);
            Assert.Contains(txs, t => t.TxId == testData.TxId);
        }

        [Theory]
        [MemberData(nameof(RefundTransactionData))]
        [HasNetworkRequests()]
        public async void CanFindRefundTransactions(
            BitcoinSwapTransactionTestData testData)
        {
            var api = CreateApi(testData.Currency, testData.Network);
            var network = ResolveNetwork(testData.Currency, testData.Network);

            var (txs, error) = await api.FindRefundsAsync(
                secretHash: testData.SecretHash,
                contractAddress: null,
                address: testData.Address,
                refundAddress: testData.RefundAddress,
                timeStamp: testData.TimeStamp,
                lockTime: testData.LockTime,
                secretSize: testData.SecretSize);

            Assert.NotNull(txs);
            Assert.Null(error);

            Assert.True(txs.Count() >= 1);
            Assert.Contains(txs, t => t.TxId == testData.TxId);
        }
    }
}