using System.Linq;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Common.Json;
using Atomix.Core.Entities;
using Atomix.Swaps.BitcoinBased;
using Newtonsoft.Json;
using Xunit;

namespace Atomix.Client.Core.Tests
{
    public class SwapTests
    {
        public const string Order = @"{
            ""Id"": 0,
            ""OrderId"": ""67fec8c7-eef6-4cd5-926f-e9b9b1d77bef"",
            ""ClientOrderId"": ""09732389-cddf-4147-85d3-3cbb8c7eae22"",
            ""UserId"": null,
            ""SymbolId"": 0,
            ""Symbol"": {
              ""Name"": ""LTC/BTC""
            },
            ""TimeStamp"": ""2019-01-22T18:47:24.1066517"",
            ""Price"": 0.0088652,
            ""LastPrice"": 0.0088652,
            ""Qty"": 1.0,
            ""LeaveQty"": 0.43599693,
            ""LastQty"": 0.56400307,
            ""Fee"": 0.0,
            ""RedeemFee"": 0.0,
            ""Side"": 1,
            ""Type"": 0,
            ""Status"": 8,
            ""EndOfTransaction"": false,
            ""SwapId"": ""27b0bad3-8453-4704-8511-922ee44dda6d"",
            ""SwapInitiative"": false,
            ""IsStayAfterDisconnect"": false,
            ""IsApproved"": false,
            ""FromWallets"": [
              {
                ""Id"": 0,
                ""CurrencyId"": 0,
                ""Currency"": {
                  ""Name"": ""LTC""
                },
                ""Address"": ""n1bx9KNjtPAmwSwQFbibc1e5uL2AEz3FTP"",
                ""PublicKey"": ""A+DhYqNNax8PdkJQKX8J62NGHOzdViJkVBHQihFTGkCm"",
                ""ProofOfPossession"": null,
                ""Nonce"": null,
                ""Orders"": []
              }
            ],
            ""ToWallet"": {
              ""Id"": 0,
              ""CurrencyId"": 0,
              ""Currency"": {
                ""Name"": ""BTC""
              },
              ""Address"": ""mj6wdhuWmh5Zhuj4idCmWmmyLufdJeUTPM"",
              ""PublicKey"": ""Ag7UecZDcwQPTTDsKl9Fq6U3DPoNawO/zkYF1li3KxLS"",
              ""ProofOfPossession"": null,
              ""Nonce"": null,
              ""Orders"": []
            },
            ""RefundWallet"": {
              ""Id"": 0,
              ""CurrencyId"": 0,
              ""Currency"": {
                ""Name"": ""LTC""
              },
              ""Address"": ""n24dWVdeqVQ7uND57JFwn7wbEi9c68nxVu"",
              ""PublicKey"": ""AirmARx+ityKRG5fXhMjShh/j8uvwm4SOYQgMnsYUAB3"",
              ""ProofOfPossession"": null,
              ""Nonce"": null,
              ""Orders"": []
            },
            ""Wallets"": null
          }";

        public const string PaymentTx1 = "010000000101d9c2f5459246eba8b3adf3bfca67ea711916342ef53ebd" +
                                         "48541c216ff2528a000000006a473044022049970d2af0edb2e5b94120" +
                                         "67ac684bd2f0e11b9591d131ed8eb2c0d99f3cee9002207447776906ee" +
                                         "f43a3453e95405bd6b846980d376ca91a1db819f047d095351da012103" +
                                         "bd89a5483eee5fd50a94a0e79ee10aa43c142fba4b762aa67781a168c7" +
                                         "94b0df0000000002d58cb802000000001976a914f8eacbe4a213b4c55f" +
                                         "516e483cdb05c9384f1df888ac20a10700000000007a635221027792ed" +
                                         "78e3acfaadf31cf7c62789ae44c1ada2d25602083f778da2bdb7d228d8" +
                                         "21020ed479c64373040f4d30ec2a5f45aba5370cfa0d6b03bfce4605d6" +
                                         "58b72b12d252ae67a914fa0a2e542f26599aa8cafd6a00d32957ba66d3" +
                                         "e78876a9142755f7d7a31c937e5ac5bfac577cc8aa5a95c84e88ac6800" +
                                         "000000";

        [Fact]
        public async void CreateBtcRedeemTxFromPaymentTxTest()
        {
            var btc = new Bitcoin();
            var address = Common.BobAddress(btc);

            var redeemAddress = new WalletAddress
            {
                Currency = Currencies.Btc,
                Address = address,
            };

            var order = JsonConvert.DeserializeObject<Order>(
                Order,
                new CompactCurrencyConverter(Currencies.Available),
                new CompactSymbolConverter(Symbols.Available));

            var paymentTx = new BitcoinBasedTransaction(btc, PaymentTx1);

            var tx = await new BitcoinBasedSwapTransactionFactory()
                .CreateSwapRedeemTxAsync(
                    paymentTx: paymentTx,
                    order: order,
                    redeemAddress: redeemAddress)
                .ConfigureAwait(false);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
        }

        public const string PaymentTx2 = "01000000015da8e62daee4f69c1ec58029e01445eee02b9a06c5baf98" +
                                         "7dd289a40f0f67504000000006a47304402203ccc7d0ef0982e847822" +
                                         "bc22cad4dc3d525bd4aa12f47b6898659c1f144d8b7702207c0c3e70e" +
                                         "4c5d0023cbfbbae64ebbe5aa5f78b3ebbf385a1eb251693795c19e801" +
                                         "210258bed3f9553895c18fbfd766960dafb04a96acdcd66676609a228" +
                                         "519177be9ed0000000002a0bb0d00000000007a635221034b2f6475ed" +
                                         "e7aa2578b1ef47342324dbfa9bd680a6a59e1f7257672854cbe54d210" +
                                         "313b447991f69f2012ab4c232078e6c0c0a4eda29318994a713d4d358" +
                                         "1f4d32fc52ae67a914f6870b2345d9bd77e3d0088e5010fc5f77b7f30" +
                                         "88876a9149d2b5f721bacbba90147b3063c4ddb2df3d26b6788ac68f0" +
                                         "030000000000001976a914500299a4008e5eea8b0f3e1c914e39ecbc8" +
                                         "b4d9388ac00000000";

        public const string RefundTx2 = "01000000011bc87e96eac96a531e1e5f76b1bfac343604f697fccfb42ba" +
                                        "6a6f67e2803b40d00000000920047304402201f285ca7fbd9aee2315e44" +
                                        "59e2fe7f883a9ef56fba2d762c5be4ab3e618f9bd8022071739dfbf1075" +
                                        "856fc65c8a1901b8da939d11ef9bf938944874de43a6d9645b401473044" +
                                        "02202759665a0389ed3e79eaf780ed4d24dfa00b205b84edf7170e178fc" +
                                        "a7d89b9a902204179ec23ba755825ef94d248451e499c29218d8c71eb3d" +
                                        "7ff37197d5a57021560151000000000130ad0d00000000001976a914500" +
                                        "299a4008e5eea8b0f3e1c914e39ecbc8b4d9388ac0b8c505c";

        [Fact]
        public void VerifyBtcRefundTxTest()
        {
            var btc = new Bitcoin();
            
            var paymentTx = new BitcoinBasedTransaction(btc, PaymentTx2);
            var swapOutputs = paymentTx.Outputs.Where(o => o.IsSwapPayment);

            var tx = new BitcoinBasedTransaction(btc, RefundTx2);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
            Assert.True(tx.Verify(swapOutputs));
        }
    }
}