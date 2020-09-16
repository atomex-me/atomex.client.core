using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Xunit;

using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;

namespace Atomex.Client.Core.Tests.BitcoinBased
{
    public class BitcoinBasedTests
    {
        public static IEnumerable<object[]> Currencies =>
            new List<object[]>
            {
                new object[] {Commons.CurrenciesMainNet.Get<Bitcoin>("BTC")},
                new object[] {Commons.CurrenciesTestNet.Get<Bitcoin>("BTC")},
                new object[] {Commons.CurrenciesMainNet.Get<Litecoin>("LTC")},
                new object[] {Commons.CurrenciesTestNet.Get<Litecoin>("LTC")}
            };

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction CreatePaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Commons.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            const int change = 9999_0000;

            var tx = currency.CreatePaymentTx(
                unspentOutputs: initTx.Outputs,
                destinationAddress: Commons.Bob.PubKey.GetAddress(currency),
                changeAddress: Commons.Alice.PubKey.GetAddress(currency),
                amount: amount,
                fee: fee,
                lockTime: DateTimeOffset.MinValue);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
            Assert.NotNull(tx.Outputs.FirstOrDefault(o => o.Value == amount));
            Assert.NotNull(tx.Outputs.FirstOrDefault(o => o.Value == change));
            Assert.Equal(tx.Outputs.First(o => o.Value == amount).DestinationAddress(currency), Commons.BobAddress(currency));
            Assert.Equal(tx.Outputs.First(o => o.Value == change).DestinationAddress(currency), Commons.AliceAddress(currency));
            Assert.Equal(initTx.TotalOut - fee, tx.TotalOut);
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction CreateSegwitPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Commons.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            const int change = 9999_0000;

            var tx = BitcoinBasedCommon.CreateSegwitPaymentTx(
                currency: currency,
                outputs: initTx.Outputs,
                from: Commons.Alice.PubKey,
                to: Commons.Bob.PubKey,
                amount: amount,
                fee: fee);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
            Assert.NotNull(tx.Outputs.FirstOrDefault(o => o.Value == amount));
            Assert.NotNull(tx.Outputs.FirstOrDefault(o => o.Value == change));
            Assert.Equal(tx.Outputs.First(o => o.Value == amount).DestinationAddress(currency), Commons.BobSegwitAddress(currency));
            Assert.Equal(initTx.TotalOut - fee, tx.TotalOut);
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction CreateP2PkSwapPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Commons.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            //const int change = 9999_0000;

            var tx = currency.CreateP2PkSwapPaymentTx(
                unspentOutputs: initTx.Outputs,
                aliceRefundPubKey: Commons.Alice.PubKey.ToBytes(),
                bobRefundPubKey: Commons.Bob.PubKey.ToBytes(),
                bobDestinationPubKey: Commons.Bob.PubKey.ToBytes(),
                secretHash: Commons.SecretHash,
                amount: amount,
                fee: fee);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
            Assert.Equal(initTx.TotalOut - fee, tx.TotalOut);
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction CreateP2PkhSwapPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Commons.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            //const int change = 9999_0000;

            var tx = currency.CreateP2PkhSwapPaymentTx(
                unspentOutputs: initTx.Outputs,
                aliceRefundPubKey: Commons.Alice.PubKey.ToBytes(),
                bobRefundPubKey: Commons.Bob.PubKey.ToBytes(),
                bobAddress: Commons.Bob.PubKey.GetAddress(currency),
                secretHash: Commons.SecretHash,
                amount: amount,
                fee: fee);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
            Assert.Equal(initTx.TotalOut - fee, tx.TotalOut);
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction CreateHtlcP2PkhSwapPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Commons.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            //const int change = 9999_0000;

            var tx = currency.CreateHtlcP2PkhSwapPaymentTx(
                unspentOutputs: initTx.Outputs,
                aliceRefundAddress: Commons.Alice.PubKey.GetAddress(currency),
                bobAddress: Commons.Bob.PubKey.GetAddress(currency),
                lockTime: DateTimeOffset.UtcNow.AddHours(1),
                secretHash: Commons.SecretHash,
                secretSize: Commons.Secret.Length,
                amount: amount,
                fee: fee);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
            Assert.Equal(initTx.TotalOut - fee, tx.TotalOut);
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public (IBitcoinBasedTransaction, byte[]) CreateHtlcP2PkhScriptSwapPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Commons.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            //const int change = 9999_0000;

            var tx = currency.CreateHtlcP2PkhScriptSwapPaymentTx(
                unspentOutputs: initTx.Outputs,
                aliceRefundAddress: Commons.Alice.PubKey.GetAddress(currency),
                bobAddress: Commons.Bob.PubKey.GetAddress(currency),
                lockTime: DateTimeOffset.UtcNow.AddHours(1),
                secretHash: Commons.SecretHash,
                secretSize: Commons.Secret.Length,
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
        public IBitcoinBasedTransaction CreateP2PkSwapRefundTx(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = CreateP2PkSwapPaymentTx(currency);
            var paymentTxOutputs = paymentTx.Outputs
                .Where(o => o.Value == paymentQty)
                .ToArray();
            var paymentTxTotal = paymentTxOutputs.Aggregate(0L, (s, output) => s + output.Value);

            var lockTime = DateTimeOffset.Now.AddHours(12);
            const int amount = 9999_0000;
            const int fee = 1_0000;
            // change = 0;

            var tx = BitcoinBasedCommon.CreatePaymentTx(
                currency: Commons.BtcTestNet,
                outputs: paymentTxOutputs,
                from: Commons.Alice.PubKey,
                to: Commons.Alice.PubKey,
                amount: amount,
                fee: fee,
                lockTime: lockTime
            );

            Assert.True(tx.Check());
            Assert.Equal(paymentTxTotal - fee, tx.TotalOut);
            Assert.Equal(fee, tx.GetFee(paymentTxOutputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Commons.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            var tx = CreatePaymentTx(currency);

            tx.Sign(Commons.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignSegwitPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Commons.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            var tx = CreateSegwitPaymentTx(currency);

            tx.Sign(Commons.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignP2PkSwapPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Commons.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            var tx = CreateP2PkSwapPaymentTx(currency);

            tx.Sign(Commons.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs, checkScriptPubKey: false));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignP2PkhSwapPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Commons.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            var tx = CreateP2PkhSwapPaymentTx(currency);

            tx.Sign(Commons.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs, checkScriptPubKey: false));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignHtlcP2PkhSwapPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Commons.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            var tx = CreateHtlcP2PkhSwapPaymentTx(currency);

            tx.Sign(Commons.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs, checkScriptPubKey: false));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public (IBitcoinBasedTransaction, byte[]) SignHtlcP2PkhScriptSwapPaymentTx(BitcoinBasedCurrency currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Commons.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            var (tx, redeemScript) = CreateHtlcP2PkhScriptSwapPaymentTx(currency);

            tx.Sign(Commons.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs));

            return (tx, redeemScript);
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignP2PkSwapRefundTx(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = CreateP2PkSwapPaymentTx(currency);
            var paymentTxOutputs = paymentTx.Outputs.Where(o => o.Value == paymentQty).ToArray();

            var lockTime = DateTimeOffset.Now.AddHours(12);
            const int amount = 9999_0000;
            const int fee = 1_0000;
            // change = 0;

            var refundTx = BitcoinBasedCommon.CreatePaymentTx(
                currency: Commons.BtcTestNet,
                outputs: paymentTxOutputs,
                from: Commons.Alice.PubKey,
                to: Commons.Alice.PubKey,
                amount: amount,
                fee: fee,
                lockTime: lockTime
            );

            var sigHash = new uint256(refundTx.GetSignatureHash(paymentTxOutputs.First()));

            var aliceSign = Commons.Alice.Sign(sigHash, SigHash.All);
            var bobSign = Commons.Bob.Sign(sigHash, SigHash.All);
            var refundScript = BitcoinBasedSwapTemplate.GenerateSwapRefund(aliceSign, bobSign);

            refundTx.NonStandardSign(refundScript, paymentTxOutputs.First());

            Assert.True(refundTx.Verify(paymentTxOutputs));

            return refundTx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignHtlcP2PkhSwapRefundTx(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = CreateHtlcP2PkhSwapPaymentTx(currency);
            var paymentTxOutputs = paymentTx.Outputs.Where(o => o.Value == paymentQty).ToArray();

            var lockTime = DateTimeOffset.UtcNow.AddHours(1);
            const int amount = 9999_0000;
            const int fee = 1_0000;
            // change = 0;

            var refundTx = BitcoinBasedCommon.CreatePaymentTx(
                currency: Commons.BtcTestNet,
                outputs: paymentTxOutputs,
                from: Commons.Alice.PubKey,
                to: Commons.Alice.PubKey,
                amount: amount,
                fee: fee,
                lockTime: lockTime
            );

            var sigHash = new uint256(refundTx.GetSignatureHash(paymentTxOutputs.First()));

            var aliceSign = Commons.Alice.Sign(sigHash, SigHash.All);

            var refundScript = BitcoinBasedSwapTemplate.GenerateHtlcSwapRefund(
                aliceRefundSig: aliceSign,
                aliceRefundPubKey: Commons.Alice.PubKey.ToBytes());

            refundTx.NonStandardSign(refundScript, paymentTxOutputs.First());

            Assert.True(refundTx.Verify(paymentTxOutputs));

            return refundTx;
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
                currency: Commons.BtcTestNet,
                outputs: paymentTxOutputs,
                from: Commons.Alice.PubKey,
                to: Commons.Alice.PubKey,
                amount: amount,
                fee: fee,
                lockTime: lockTime,
                new Script(redeemScript)
            );

            var sigHash = new uint256(refundTx.GetSignatureHash(new Script(redeemScript), paymentTxOutputs.First()));

            var aliceSign = Commons.Alice.Sign(sigHash, SigHash.All);

            var refundScript = BitcoinBasedSwapTemplate.GenerateHtlcSwapRefundForP2Sh(
                aliceRefundSig: aliceSign.ToBytes(),
                aliceRefundPubKey: Commons.Alice.PubKey.ToBytes(),
                redeemScript: redeemScript);

            refundTx.NonStandardSign(refundScript, paymentTxOutputs.First());

            Assert.True(refundTx.Verify(paymentTxOutputs));

            return refundTx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignP2PkSwapRedeemTx(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = CreateP2PkSwapPaymentTx(currency);
            var paymentTxOutputs = paymentTx.Outputs
                .Where(o => o.Value == paymentQty)
                .ToArray();

            const int amount = 9999_0000;
            const int fee = 1_0000;

            var redeemTx = currency.CreatePaymentTx(
                unspentOutputs: paymentTxOutputs,
                destinationAddress: Commons.Bob.PubKey.GetAddress(currency),
                changeAddress: Commons.Bob.PubKey.GetAddress(currency),
                amount: amount,
                fee: fee,
                lockTime: DateTimeOffset.MinValue);

            var sigHash = new uint256(redeemTx.GetSignatureHash(paymentTxOutputs.First()));
            var bobSign = Commons.Bob.Sign(sigHash, SigHash.All);
            var redeemScript = BitcoinBasedSwapTemplate.GenerateP2PkSwapRedeem(bobSign, Commons.Secret);

            redeemTx.NonStandardSign(redeemScript, paymentTxOutputs.First());

            Assert.True(redeemTx.Verify(paymentTxOutputs));

            return redeemTx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignP2PkhSwapRedeemTx(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = CreateP2PkhSwapPaymentTx(currency);
            var paymentTxOutputs = paymentTx.Outputs
                .Where(o => o.Value == paymentQty)
                .ToArray();

            const int amount = 9999_0000;
            const int fee = 1_0000;

            var redeemTx = currency.CreatePaymentTx(
                unspentOutputs: paymentTxOutputs,
                destinationAddress: Commons.Bob.PubKey.GetAddress(currency),
                changeAddress: Commons.Bob.PubKey.GetAddress(currency),
                amount: amount,
                fee: fee,
                lockTime: DateTimeOffset.MinValue);

            var sigHash = new uint256(redeemTx.GetSignatureHash(paymentTxOutputs.First()));
            var bobSign = Commons.Bob.Sign(sigHash, SigHash.All).ToBytes();
            var bobPubKey = Commons.Bob.PubKey.ToBytes();
            var redeemScript = BitcoinBasedSwapTemplate.GenerateP2PkhSwapRedeem(bobSign, bobPubKey, Commons.Secret);

            redeemTx.NonStandardSign(redeemScript, paymentTxOutputs.First());

            Assert.True(redeemTx.Verify(paymentTxOutputs));

            return redeemTx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public IBitcoinBasedTransaction SignHtlcP2PkhSwapRedeemTx(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = CreateHtlcP2PkhSwapPaymentTx(currency);
            var paymentTxOutputs = paymentTx.Outputs
                .Where(o => o.Value == paymentQty)
                .ToArray();

            const int amount = 9999_0000;
            const int fee = 1_0000;

            var redeemTx = currency.CreatePaymentTx(
                unspentOutputs: paymentTxOutputs,
                destinationAddress: Commons.Bob.PubKey.GetAddress(currency),
                changeAddress: Commons.Bob.PubKey.GetAddress(currency),
                amount: amount,
                fee: fee,
                lockTime: DateTimeOffset.MinValue);

            var sigHash = new uint256(redeemTx.GetSignatureHash(paymentTxOutputs.First()));
            var bobSign = Commons.Bob.Sign(sigHash, SigHash.All).ToBytes();
            var bobPubKey = Commons.Bob.PubKey.ToBytes();
            var redeemScript = BitcoinBasedSwapTemplate.GenerateHtlcP2PkhSwapRedeem(bobSign, bobPubKey, Commons.Secret);

            redeemTx.NonStandardSign(redeemScript, paymentTxOutputs.First());

            Assert.True(redeemTx.Verify(paymentTxOutputs));

            return redeemTx;
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
                destinationAddress: Commons.Bob.PubKey.GetAddress(currency),
                changeAddress: Commons.Bob.PubKey.GetAddress(currency),
                amount: amount,
                fee: fee,
                lockTime: DateTimeOffset.MinValue,
                new Script(redeemScript));

            var sigHash = new uint256(redeemTx.GetSignatureHash(new Script(redeemScript), paymentTxOutputs.First()));

            var scriptSig = BitcoinBasedSwapTemplate.GenerateP2PkhSwapRedeemForP2Sh(
                sig: Commons.Bob.Sign(sigHash, SigHash.All).ToBytes(),
                pubKey: Commons.Bob.PubKey.ToBytes(),
                secret: Commons.Secret,
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
                destinationAddress: Commons.Bob
                    .PubKey
                    .GetSegwitAddress(currency.Network)
                    .ToString(),
                changeAddress: Commons.Bob
                    .PubKey
                    .GetSegwitAddress(currency.Network)
                    .ToString(),
                amount: 9999_0000,
                fee: 1_0000);

            spentTx.Sign(Commons.Bob, paymentTxOutputs);

            Assert.True(spentTx.Verify(paymentTxOutputs));
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public void ExtractSecretFromHtlcP2PkhScriptSwapRedeemTx(BitcoinBasedCurrency currency)
        {
            var tx = SignHtlcP2PkhScriptSwapRedeemTx(currency);

            var data = (tx.Inputs.First() as BitcoinBasedTxPoint)
                .ExtractAllPushData();

            var secret = data.FirstOrDefault(d => d.SequenceEqual(Commons.Secret));

            Assert.NotNull(secret);
        }
    }
}