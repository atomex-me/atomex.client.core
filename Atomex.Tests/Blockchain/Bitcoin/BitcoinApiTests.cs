using System.Collections.Generic;
using System.Linq;

using NBitcoin;
using Xunit;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinTransactionTestData
    {
        public string Network { get; set; }
        public string Currency { get; set; }
        public string TxId { get; set; }
        public int BlockHeight { get; set; }
        public List<BitcoinTxInput> Inputs { get; set; }
        public List<BitcoinTxOutput> Outputs { get; set; }
    }

    public class BitcoinAddressTestData
    {
        public string Network { get; set; }
        public string Currency { get; set; }
        public string Address { get; set; }
        public List<BitcoinTxOutput> Outputs { get; set; }
    }

    public abstract class BitcoinApiTests
    {
        public static readonly IEnumerable<object[]> TransactionData = new List<object[]>
        {
            new object[]
            {
                new BitcoinTransactionTestData
                {
                    Network     = "BTC",
                    Currency    = "BTC",
                    TxId        = "f6a33e1198d40dddd6ff472186e381bfaae0f15d7de7bfe00793d7232a8f2fd6",
                    BlockHeight = 640543,
                    Inputs      = new List<BitcoinTxInput>
                    {
                        new BitcoinTxInput
                        {
                            Index = 0,
                            PreviousOutput = new BitcoinTxPoint
                            {
                                Hash = "aff22d2e6bd6dec104a304797e064ea523941ae3d8d11eccab31b512c2d26427",
                                Index = 1
                            },
                            ScriptSig = "483045022100d474f597fc815e3cbaad8f013f6deb66dc0bbaa5ec3efecdcdbf845265ed65d3022060fa2fbd2f0ebdd6e0606b3def14dd854f416cc68261bfad3d9941a2dffd6f58012103ee62752e41353642b0a26a51b5c1398ef2df383a9075f5f93ca1a15c7ee5cf0d"
                        }
                    },
                    Outputs     = new List<BitcoinTxOutput>
                    {
                        new BitcoinTxOutput
                        {
                            Address = "3D5sQHmjLpni9nB5YViPKRzRUDBPbZfXNk",
                            Coin = new Coin(
                                fromTxHash: uint256.Parse("f6a33e1198d40dddd6ff472186e381bfaae0f15d7de7bfe00793d7232a8f2fd6"),
                                fromOutputIndex: 0,
                                amount: Money.Satoshis(50376819),
                                scriptPubKey: Script.FromHex("a9147cfbd6565af6bc80e0aa31d3547e0dc5a093994a87"))
                        },
                        new BitcoinTxOutput
                        {
                            Address = "19Nc924FvM1dV2rJJuv2vu8ZY881vc4pmp",
                            Coin = new Coin(
                                fromTxHash: uint256.Parse("f6a33e1198d40dddd6ff472186e381bfaae0f15d7de7bfe00793d7232a8f2fd6"),
                                fromOutputIndex: 1,
                                amount: Money.Satoshis(2101172855),
                                scriptPubKey: Script.FromHex("76a9145bd712ca2ee65b693d97c3717c908a16088615d288ac"))
                        }
                    }
                },
            },
            new object[]
            {
                new BitcoinTransactionTestData
                {
                    Network     = "BTC",
                    Currency    = "BTC",
                    TxId        = "19379d4e1a5d3bbb9f266c1481465a5d9ce7a5b6b9345235cee0e4f358b25b7f",
                    BlockHeight = 680006,
                    Inputs      = new List<BitcoinTxInput>
                    {
                        new BitcoinTxInput
                        {
                            Index = 0,
                            PreviousOutput = new BitcoinTxPoint
                            {
                                Hash = "e99bdb5eb4d2fd0477ca8a4b29f4468912c7ed5754e19be838246b843efab454",
                                Index = 9
                            },
                            ScriptSig = "473044022018f40d405d07c7e1f0060660c09f326bd76c54f565fc8aaf24399c5532fdb0750220512a5dfdd4b6f9d09963d6b72d1d3aea7e3defc05d48adff2bf3add2de33827c01210204fb644cd2ef1d6e5330c6a10e5818c39c5c823f7a2d31984076b60969f85d34"
                        }
                    },
                    Outputs     = new List<BitcoinTxOutput>
                    {
                        new BitcoinTxOutput
                        {
                            Address = "1KeyxnydRq4ttPhvWbscK4TTAToSrSbasJ",
                            Coin = new Coin(
                                fromTxHash: uint256.Parse("19379d4e1a5d3bbb9f266c1481465a5d9ce7a5b6b9345235cee0e4f358b25b7f"),
                                fromOutputIndex: 0,
                                amount: Money.Satoshis(81793735),
                                scriptPubKey: Script.FromHex("76a914cca13441d78435e6c34d9d7ddeaa49827215230e88ac"))
                        },
                        new BitcoinTxOutput
                        {
                            Address = "3BamcwwfbW6RLyxVTCxscJ1nAX6gLYWuox",
                            Coin = new Coin(
                                fromTxHash: uint256.Parse("19379d4e1a5d3bbb9f266c1481465a5d9ce7a5b6b9345235cee0e4f358b25b7f"),
                                fromOutputIndex: 1,
                                amount: Money.Satoshis(143084585),
                                scriptPubKey: Script.FromHex("a9146c82d27c8106d054809eaf5d25d1a78c4e6697ea87"))
                        }
                    }
                },
            }
        };

        public static readonly IEnumerable<object[]> OutputsData = new List<object[]>
        {
            new object[]
            {
                new BitcoinAddressTestData
                {
                    Network  = "BTC",
                    Currency = "BTC",
                    Address  = "3BamcwwfbW6RLyxVTCxscJ1nAX6gLYWuox",
                    Outputs  = new List<BitcoinTxOutput>
                    {
                        new BitcoinTxOutput
                        {
                            Currency = "BTC",
                            Address = "3BamcwwfbW6RLyxVTCxscJ1nAX6gLYWuox",
                            Coin = new Coin(
                                fromTxHash: uint256.Parse("19379d4e1a5d3bbb9f266c1481465a5d9ce7a5b6b9345235cee0e4f358b25b7f"),
                                fromOutputIndex: 1,
                                amount: Money.Satoshis(143084585),
                                scriptPubKey: Script.FromHex("a9146c82d27c8106d054809eaf5d25d1a78c4e6697ea87")),
                             IsConfirmed = true,
                             IsSpentConfirmed = true,
                             SpentTxPoints = new List<BitcoinTxPoint>
                             {
                                 new BitcoinTxPoint
                                 {
                                     Hash = "a3f3a7ca5f68109b1a6231f9a5e9cf2efe55b80bb5de90b25f187f62aff7802b",
                                     Index = 0
                                 }
                             }
                        }
                    }
                }
            }
        };

        public abstract IBitcoinApi CreateApi(string currency, string network);
        public abstract Network ResolveNetwork(string currency, string network);

        [Theory]
        [MemberData(nameof(TransactionData))]
        [HasNetworkRequests()]
        public async void CanGetTransaction(
            BitcoinTransactionTestData testData)
        {
            var api = CreateApi(testData.Currency, testData.Network);

            var (tx, error) = await api
                .GetTransactionAsync(testData.TxId);

            Assert.NotNull(tx);
            Assert.Null(error);

            var bitcoinTx = tx as BitcoinTransaction;

            Assert.NotNull(bitcoinTx);
            Assert.Equal(testData.Currency, bitcoinTx.Currency);
            Assert.Equal(testData.TxId, bitcoinTx.Id);
            Assert.Equal(testData.TxId, bitcoinTx.TxId);
            Assert.Equal(testData.BlockHeight, bitcoinTx.BlockHeight);
            Assert.Equal(TransactionStatus.Confirmed, bitcoinTx.Status);
            Assert.True(bitcoinTx.Confirmations > 0);
            Assert.NotNull(bitcoinTx.CreationTime);
            Assert.NotNull(bitcoinTx.BlockTime);

            var inputs = bitcoinTx.Inputs.ToList();

            Assert.Equal(testData.Inputs.Count, inputs.Count);

            for (var i = 0; i < inputs.Count; ++i)
            {
                Assert.Equal(testData.Inputs[i].Index, inputs[i].Index);
                Assert.Equal(testData.Inputs[i].ScriptSig, inputs[i].ScriptSig);
                Assert.Equal(testData.Inputs[i].PreviousOutput.Hash, inputs[i].PreviousOutput.Hash);
                Assert.Equal(testData.Inputs[i].PreviousOutput.Index, inputs[i].PreviousOutput.Index);
            }

            var outputs = bitcoinTx.Outputs(ResolveNetwork(testData.Currency, testData.Network)).ToList();

            Assert.Equal(testData.Outputs.Count, outputs.Count);

            for (var o = 0; o < outputs.Count; ++o)
            {
                Assert.Equal(testData.Outputs[o].Address, outputs[o].Address);
                Assert.Equal(testData.Outputs[o].Value, outputs[o].Value);
            }
        }

        [Theory]
        [MemberData(nameof(OutputsData))]
        [HasNetworkRequests()]
        public async void CanGetOutputs(
            BitcoinAddressTestData testData)
        {
            var api = CreateApi(testData.Currency, testData.Network);

            var (outputs, error) = await api
                .GetOutputsAsync(testData.Address);

            Assert.NotNull(outputs);
            Assert.Null(error);

            Assert.True(outputs.Count() >= testData.Outputs.Count);

            foreach (var testOutput in testData.Outputs)
            {
                var output = outputs
                    .FirstOrDefault(o => o.TxId == testOutput.TxId && o.Index == testOutput.Index);

                Assert.NotNull(output);

                Assert.Equal(testOutput.Address, output.Address);
                Assert.Equal(testOutput.Currency, output.Currency);
                Assert.Equal(testOutput.Index, output.Index);
                Assert.Equal(testOutput.IsConfirmed, output.IsConfirmed);
                Assert.Equal(testOutput.IsSegWit, output.IsSegWit);
                Assert.Equal(testOutput.IsSpent, output.IsSpent);
                Assert.Equal(testOutput.IsSpentConfirmed, output.IsSpentConfirmed);
                Assert.Equal(testOutput.TxId, output.TxId);
                Assert.Equal(testOutput.Value, output.Value);
                Assert.Equal(testOutput.Type, output.Type);
                Assert.Equal(testOutput.Coin.ScriptPubKey.ToHex(), output.Coin.ScriptPubKey.ToHex());

                if (testOutput.SpentTxPoints != null)
                {
                    Assert.NotNull(output.SpentTxPoints);

                    Assert.Equal(testOutput.SpentTxPoints.First().Hash, output.SpentTxPoints.First().Hash);
                    Assert.Equal(testOutput.SpentTxPoints.First().Index, output.SpentTxPoints.First().Index);
                }
            }
        }
    }
}