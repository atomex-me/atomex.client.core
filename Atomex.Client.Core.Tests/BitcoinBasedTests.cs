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
        public static IEnumerable<object[]> BitcoinBasedCurrencies =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesMainNet.Get<Bitcoin>()},
                new object[] {Common.CurrenciesTestNet.Get<Bitcoin>()},
                new object[] {Common.CurrenciesMainNet.Get<Litecoin>()},
                new object[] {Common.CurrenciesTestNet.Get<Litecoin>()}
            };

        public IBitcoinBasedTransaction CreateFakeTx(BitcoinBasedCurrency currency, PubKey destination)
        {
            const int output1 = 1_0000_0000;
            const int output2 = 1_0000_0000;

            var tx = BitcoinBasedCommon.CreateFakeTx(currency, destination, output1, output2);

            Assert.NotNull(tx);
            Assert.Equal(output1 + output2, tx.TotalOut);
  
            return tx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction CreatePaymentTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            var initTx = CreateFakeTx(currency, Common.Alice.PubKey);
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
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction CreateSegwitPaymentTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            var initTx = CreateFakeTx(currency, Common.Alice.PubKey);
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
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction CreateP2PkSwapPaymentTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            var initTx = CreateFakeTx(currency, Common.Alice.PubKey);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            //const int change = 9999_0000;

            var tx = currency.CreateP2PkSwapPaymentTx(
                unspentOutputs: initTx.Outputs,
                aliceRefundPubKey: Common.Alice.PubKey.ToBytes(),
                bobRefundPubKey: Common.Bob.PubKey.ToBytes(),
                bobDestinationPubKey: Common.Bob.PubKey.ToBytes(),
                secretHash: Common.SecretHash,
                amount: amount,
                fee: fee);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
            Assert.Equal(initTx.TotalOut - fee, tx.TotalOut);
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction CreateP2PkhSwapPaymentTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            var initTx = CreateFakeTx(currency, Common.Alice.PubKey);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            //const int change = 9999_0000;

            var tx = currency.CreateP2PkhSwapPaymentTx(
                unspentOutputs: initTx.Outputs,
                aliceRefundPubKey: Common.Alice.PubKey.ToBytes(),
                bobRefundPubKey: Common.Bob.PubKey.ToBytes(),
                bobAddress: Common.Bob.PubKey.GetAddress(currency),
                secretHash: Common.SecretHash,
                amount: amount,
                fee: fee);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
            Assert.Equal(initTx.TotalOut - fee, tx.TotalOut);
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction CreateHtlcP2PkhSwapPaymentTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            var initTx = CreateFakeTx(currency, Common.Alice.PubKey);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            //const int change = 9999_0000;

            var tx = currency.CreateHtlcP2PkhSwapPaymentTx(
                unspentOutputs: initTx.Outputs,
                aliceRefundAddress: Common.Alice.PubKey.GetAddress(currency),
                bobAddress: Common.Bob.PubKey.GetAddress(currency),
                lockTime: DateTimeOffset.UtcNow.AddHours(1),
                secretHash: Common.SecretHash,
                secretSize: Common.Secret.Length,
                amount: amount,
                fee: fee);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
            Assert.Equal(initTx.TotalOut - fee, tx.TotalOut);
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public (IBitcoinBasedTransaction, byte[]) CreateHtlcP2PkhScriptSwapPaymentTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            var initTx = CreateFakeTx(currency, Common.Alice.PubKey);
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
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction CreateP2PkSwapRefundTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = CreateP2PkSwapPaymentTxAlice2Bob(currency);
            var paymentTxOutputs = paymentTx.Outputs
                .Where(o => o.Value == paymentQty)
                .ToArray();
            var paymentTxTotal = paymentTxOutputs.Aggregate(0L, (s, output) => s + output.Value);

            var lockTime = DateTimeOffset.Now.AddHours(12);
            const int amount = 9999_0000;
            const int fee = 1_0000;
            // change = 0;

            var tx = BitcoinBasedCommon.CreatePaymentTx(
                currency: Common.BtcTestNet, 
                outputs: paymentTxOutputs,
                from: Common.Alice.PubKey,
                to: Common.Alice.PubKey,
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
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction SignPaymentTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            var initTx = CreateFakeTx(currency, Common.Alice.PubKey);
            var tx = CreatePaymentTxAlice2Bob(currency);

            tx.Sign(Common.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction SignSegwitPaymentTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            var initTx = CreateFakeTx(currency, Common.Alice.PubKey);
            var tx = CreateSegwitPaymentTxAlice2Bob(currency);

            tx.Sign(Common.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction SignP2PkSwapPaymentTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            var initTx = CreateFakeTx(currency, Common.Alice.PubKey);
            var tx = CreateP2PkSwapPaymentTxAlice2Bob(currency);

            tx.Sign(Common.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs, checkScriptPubKey: false));

            return tx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction SignP2PkhSwapPaymentTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            var initTx = CreateFakeTx(currency, Common.Alice.PubKey);
            var tx = CreateP2PkhSwapPaymentTxAlice2Bob(currency);

            tx.Sign(Common.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs, checkScriptPubKey: false));

            return tx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction SignHtlcP2PkhSwapPaymentTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            var initTx = CreateFakeTx(currency, Common.Alice.PubKey);
            var tx = CreateHtlcP2PkhSwapPaymentTxAlice2Bob(currency);

            tx.Sign(Common.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs, checkScriptPubKey: false));

            return tx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public (IBitcoinBasedTransaction, byte[]) SignHtlcP2PkhScriptSwapPaymentTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            var initTx = CreateFakeTx(currency, Common.Alice.PubKey);
            var (tx, redeemScript) = CreateHtlcP2PkhScriptSwapPaymentTxAlice2Bob(currency);

            tx.Sign(Common.Alice, initTx.Outputs);

            Assert.True(tx.Verify(initTx.Outputs));

            return (tx, redeemScript);
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction SignP2PkSwapRefundTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = CreateP2PkSwapPaymentTxAlice2Bob(currency);
            var paymentTxOutputs = paymentTx.Outputs.Where(o => o.Value == paymentQty).ToArray();

            var lockTime = DateTimeOffset.Now.AddHours(12);
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

            var sigHash = new uint256(refundTx.GetSignatureHash(paymentTxOutputs.First()));

            var aliceSign = Common.Alice.Sign(sigHash, SigHash.All);
            var bobSign = Common.Bob.Sign(sigHash, SigHash.All);
            var refundScript = BitcoinBasedSwapTemplate.GenerateSwapRefund(aliceSign, bobSign);

            refundTx.NonStandardSign(refundScript, paymentTxOutputs.First());

            Assert.True(refundTx.Verify(paymentTxOutputs));

            return refundTx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction SignHtlcP2PkhSwapRefundTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = CreateHtlcP2PkhSwapPaymentTxAlice2Bob(currency);
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

            var sigHash = new uint256(refundTx.GetSignatureHash(paymentTxOutputs.First()));

            var aliceSign = Common.Alice.Sign(sigHash, SigHash.All);

            var refundScript = BitcoinBasedSwapTemplate.GenerateHtlcSwapRefund(
                aliceRefundSig: aliceSign,
                aliceRefundPubKey: Common.Alice.PubKey.ToBytes());

            refundTx.NonStandardSign(refundScript, paymentTxOutputs.First());

            Assert.True(refundTx.Verify(paymentTxOutputs));

            return refundTx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction SignHtlcP2PkhScriptSwapRefundTxAlice2Bob(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var (paymentTx, redeemScript) = CreateHtlcP2PkhScriptSwapPaymentTxAlice2Bob(currency);
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
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction SignP2PkSwapRedeemTxByBob(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = CreateP2PkSwapPaymentTxAlice2Bob(currency);
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

            var sigHash = new uint256(redeemTx.GetSignatureHash(paymentTxOutputs.First()));
            var bobSign = Common.Bob.Sign(sigHash, SigHash.All);
            var redeemScript = BitcoinBasedSwapTemplate.GenerateP2PkSwapRedeem(bobSign, Common.Secret);

            redeemTx.NonStandardSign(redeemScript, paymentTxOutputs.First());

            Assert.True(redeemTx.Verify(paymentTxOutputs));

            return redeemTx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction SignP2PkhSwapRedeemTxByBob(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = CreateP2PkhSwapPaymentTxAlice2Bob(currency);
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

            var sigHash = new uint256(redeemTx.GetSignatureHash(paymentTxOutputs.First()));
            var bobSign = Common.Bob.Sign(sigHash, SigHash.All).ToBytes();
            var bobPubKey = Common.Bob.PubKey.ToBytes();
            var redeemScript = BitcoinBasedSwapTemplate.GenerateP2PkhSwapRedeem(bobSign, bobPubKey, Common.Secret);

            redeemTx.NonStandardSign(redeemScript, paymentTxOutputs.First());

            Assert.True(redeemTx.Verify(paymentTxOutputs));

            return redeemTx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction SignHtlcP2PkhSwapRedeemTxByBob(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = CreateHtlcP2PkhSwapPaymentTxAlice2Bob(currency);
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

            var sigHash = new uint256(redeemTx.GetSignatureHash(paymentTxOutputs.First()));
            var bobSign = Common.Bob.Sign(sigHash, SigHash.All).ToBytes();
            var bobPubKey = Common.Bob.PubKey.ToBytes();
            var redeemScript = BitcoinBasedSwapTemplate.GenerateHtlcP2PkhSwapRedeem(bobSign, bobPubKey, Common.Secret);

            redeemTx.NonStandardSign(redeemScript, paymentTxOutputs.First());

            Assert.True(redeemTx.Verify(paymentTxOutputs));

            return redeemTx;
        }

        [Theory]
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public IBitcoinBasedTransaction SignHtlcP2PkhScriptSwapRedeemTxByBob(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var (paymentTx, redeemScript) = CreateHtlcP2PkhScriptSwapPaymentTxAlice2Bob(currency);
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
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public void SpentSegwitPaymentTxByBob(BitcoinBasedCurrency currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = SignSegwitPaymentTxAlice2Bob(currency);
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
        [MemberData(nameof(BitcoinBasedCurrencies))]
        public void ExtractSecretFromHtlcP2PkhScriptSwapRedeemTx(BitcoinBasedCurrency currency)
        {
            var tx = SignHtlcP2PkhScriptSwapRedeemTxByBob(currency);

            var secret = tx.Inputs.First().ExtractSecret();

            Assert.NotNull(secret);
            Assert.Equal(Common.Secret, secret);
        }
    }
}