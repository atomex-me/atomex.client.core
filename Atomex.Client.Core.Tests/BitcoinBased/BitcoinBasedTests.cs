using System;
using System.Collections.Generic;
using System.Linq;
using Atomex.Blockchain;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using NBitcoin;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class BitcoinBasedTests
    {
        public static IEnumerable<object[]> Currencies =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesMainNet.Get<Bitcoin>("BTC")},
                new object[] {Common.CurrenciesTestNet.Get<Bitcoin>("BTC")},
                new object[] {Common.CurrenciesMainNet.Get<Litecoin>("LTC")},
                new object[] {Common.CurrenciesTestNet.Get<Litecoin>("LTC")}
            };

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction CreatePaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            const int change = 9999_0000;

            var tx = currency.CreatePaymentTx(
                unspentOutputs: initTx.Outputs,
                destinationAddress: Common.Bob.PubKey.GetAddress(currency),
                changeAddress: Common.Alice.PubKey.GetAddress(currency),
                amount: amount,
                fee: fee,
                lockTime: DateTimeOffset.MinValue);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
            Assert.NotNull(tx.Outputs.FirstOrDefault(o => o.Value == amount));
            Assert.NotNull(tx.Outputs.FirstOrDefault(o => o.Value == change));
            Assert.Equal(tx.Outputs.First(o => o.Value == amount).DestinationAddress(currency), Common.BobAddress(currency));
            Assert.Equal(tx.Outputs.First(o => o.Value == change).DestinationAddress(currency), Common.AliceAddress(currency));
            Assert.Equal(initTx.TotalOut - fee, tx.TotalOut);
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction CreateSegwitPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            const int change = 9999_0000;

            var tx = BitcoinBasedCommon.CreateSegwitPaymentTx(
                currency: currency,
                outputs: initTx.Outputs,
                from: Common.Alice.PubKey,
                to: Common.Bob.PubKey,
                amount: amount,
                fee: fee);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
            Assert.NotNull(tx.Outputs.FirstOrDefault(o => o.Value == amount));
            Assert.NotNull(tx.Outputs.FirstOrDefault(o => o.Value == change));
            Assert.Equal(tx.Outputs.First(o => o.Value == amount).DestinationAddress(currency), Common.BobSegwitAddress(currency));
            Assert.Equal(initTx.TotalOut - fee, tx.TotalOut);
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public (IBitcoinBasedTransaction, byte[]) CreateHtlcP2PkhScriptSwapPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            //const int change = 9999_0000;

            var tx = currency.CreateHtlcP2PkhScriptSwapPaymentTx(
                unspentOutputs: initTx.Outputs,
                aliceRefundAddress: Common.Alice.PubKey.GetAddress(currency),
                bobAddress: Common.Bob.PubKey.GetAddress(currency),
                lockTime: DateTimeOffset.UtcNow.AddHours(1),
                secretHash: Common.SecretHash,
                secretSize: Common.Secret.Length,
                amount: amount,
                fee: fee,
                redeemScript: out var redeemScript);

            Assert.NotNull(tx);
            Assert.NotNull(redeemScript);
            Assert.True(tx.Check());
            Assert.Equal(initTx.TotalOut - fee, tx.TotalOut);
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return (tx, redeemScript);
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            var tx = CreatePaymentTx(currency);

            tx.Sign(Common.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignSegwitPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            var tx = CreateSegwitPaymentTx(currency);

            tx.Sign(Common.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public (IBitcoinBasedTransaction, byte[]) SignHtlcP2PkhScriptSwapPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            var (tx, redeemScript) = CreateHtlcP2PkhScriptSwapPaymentTx(currency);

            tx.Sign(Common.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs));

            return (tx, redeemScript);
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignHtlcP2PkhScriptSwapRefundTx(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var (paymentTx, redeemScript) = CreateHtlcP2PkhScriptSwapPaymentTx(currency);
            var paymentTxOutputs = paymentTx.Outputs.Where(o => o.Value == paymentQty).ToArray();

            var lockTime = DateTimeOffset.UtcNow.AddHours(1);
            const int amount = 9999_0000;
            const int fee = 1_0000;
            // change = 0;

            var refundTx = BitcoinBasedCommon.CreatePaymentTx(
                currency: Common.BtcTestNet,
                outputs: paymentTxOutputs,
                from: Common.Alice.PubKey,
                to: Common.Alice.PubKey,
                amount: amount,
                fee: fee,
                lockTime: lockTime
            );

            var sigHash = new uint256(refundTx.GetSignatureHash(new Script(redeemScript), paymentTxOutputs.First()));

            var aliceSign = Common.Alice.Sign(sigHash, SigHash.All);

            var refundScript = BitcoinBasedSwapTemplate.GenerateHtlcSwapRefundForP2Sh(
                aliceRefundSig: aliceSign.ToBytes(),
                aliceRefundPubKey: Common.Alice.PubKey.ToBytes(),
                redeemScript: redeemScript);

            refundTx.NonStandardSign(refundScript, paymentTxOutputs.First());

            Assert.True(refundTx.Verify(paymentTxOutputs));

            return refundTx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignHtlcP2PkhScriptSwapRedeemTx(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var (paymentTx, redeemScript) = CreateHtlcP2PkhScriptSwapPaymentTx(currency);
            var paymentTxOutputs = paymentTx.Outputs
                .Where(o => o.Value == paymentQty)
                .ToArray();

            const int amount = 9999_0000;
            const int fee = 1_0000;

            var redeemTx = currency.CreatePaymentTx(
                unspentOutputs: paymentTxOutputs,
                destinationAddress: Common.Bob.PubKey.GetAddress(currency),
                changeAddress: Common.Bob.PubKey.GetAddress(currency),
                amount: amount,
                fee: fee,
                lockTime: DateTimeOffset.MinValue);

            var sigHash = new uint256(redeemTx.GetSignatureHash(new Script(redeemScript), paymentTxOutputs.First()));

            var scriptSig = BitcoinBasedSwapTemplate.GenerateP2PkhSwapRedeemForP2Sh(
                sig: Common.Bob.Sign(sigHash, SigHash.All).ToBytes(),
                pubKey: Common.Bob.PubKey.ToBytes(),
                secret: Common.Secret,
                redeemScript: redeemScript);

            redeemTx.NonStandardSign(scriptSig, paymentTxOutputs.First());

            Assert.True(redeemTx.Verify(paymentTxOutputs));

            return redeemTx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public void SpentSegwitPaymentTx(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = SignSegwitPaymentTx(currency);
            var paymentTxOutputs = paymentTx.Outputs
                .Where(o => o.Value == paymentQty)
                .ToArray();

            var spentTx = currency.CreateP2WPkhTx(
                unspentOutputs: paymentTxOutputs,
                destinationAddress: Common.Bob
                    .PubKey
                    .GetSegwitAddress(currency.Network)
                    .ToString(),
                changeAddress: Common.Bob
                    .PubKey
                    .GetSegwitAddress(currency.Network)
                    .ToString(),
                amount: 9999_0000,
                fee: 1_0000);

            spentTx.Sign(Common.Bob, paymentTxOutputs);

            Assert.True(spentTx.Verify(paymentTxOutputs));
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public void ExtractSecretFromHtlcP2PkhScriptSwapRedeemTx(BitcoinBasedCurrency currency)
        {
            var tx = SignHtlcP2PkhScriptSwapRedeemTx(currency);

            var data = (tx.Inputs.First() as BitcoinBasedTxPoint)
                .ExtractAllPushData();

            var secret = data.FirstOrDefault(d => d.SequenceEqual(Common.Secret));

            Assert.NotNull(secret);
        }
    }
}