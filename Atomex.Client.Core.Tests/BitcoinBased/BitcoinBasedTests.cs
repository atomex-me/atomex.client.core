using System;
using System.Collections.Generic;
using System.Linq;

using NBitcoin;
using Xunit;

using Atomex.Blockchain.Bitcoin;
using Atomex.Common;

namespace Atomex.Client.Core.Tests
{
    public class BitcoinBasedTests
    {
        public static IEnumerable<object[]> Currencies =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesMainNet.Get<BitcoinConfig>("BTC")},
                new object[] {Common.CurrenciesTestNet.Get<BitcoinConfig>("BTC")},
                new object[] {Common.CurrenciesMainNet.Get<LitecoinConfig>("LTC")},
                new object[] {Common.CurrenciesTestNet.Get<LitecoinConfig>("LTC")}
            };

        [Theory]
        [MemberData(nameof(Currencies))]
        public BitcoinTransaction CreatePaymentTx(BitcoinBasedConfig currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            const int change = 9999_0000;

            var tx = currency.CreateTransaction(
                unspentOutputs: initTx.Outputs,
                destinationAddress: Common.Bob.PubKey.GetAddress(ScriptPubKeyType.Legacy, currency.Network).ToString(),
                changeAddress: Common.Alice.PubKey.GetAddress(ScriptPubKeyType.Legacy, currency.Network).ToString(),
                amount: amount,
                fee: fee);

            Assert.NotNull(tx);
            Assert.True(tx.Check());
            Assert.NotNull(tx.Outputs.FirstOrDefault(o => o.Value == amount));
            Assert.NotNull(tx.Outputs.FirstOrDefault(o => o.Value == change));
            Assert.Equal(tx.Outputs.First(o => o.Value == amount).DestinationAddress(currency.Network), Common.BobAddress(currency));
            Assert.Equal(tx.Outputs.First(o => o.Value == change).DestinationAddress(currency.Network), Common.AliceAddress(currency));
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public BitcoinTransaction CreateSegwitPaymentTx(BitcoinBasedConfig currency)
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
            Assert.Equal(tx.Outputs.First(o => o.Value == amount).DestinationAddress(currency.Network), Common.BobSegwitAddress(currency));
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public (BitcoinTransaction, byte[]) CreateHtlcP2PkhScriptSwapPaymentTx(BitcoinBasedConfig currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            const int amount = 1_0000_0000;
            const int fee = 1_0000;
            //const int change = 9999_0000;

            var tx = currency.CreateHtlcSegwitScriptSwapPaymentTx(
                unspentOutputs: initTx.Outputs,
                aliceRefundAddress: Common.Alice.PubKey.GetAddress(ScriptPubKeyType.Legacy, currency.Network).ToString(),
                bobAddress: Common.Bob.PubKey.GetAddress(ScriptPubKeyType.Legacy, currency.Network).ToString(),
                lockTime: Common.LockTime,
                secretHash: Common.SecretHash,
                secretSize: Common.Secret.Length,
                amount: amount,
                fee: fee,
                redeemScript: out var redeemScript);

            Assert.NotNull(tx);
            Assert.NotNull(redeemScript);
            Assert.True(tx.Check());
            Assert.Equal(fee, tx.GetFee(initTx.Outputs));

            return (tx, redeemScript);
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public BitcoinTransaction SignPaymentTx(BitcoinBasedConfig currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            var tx = CreatePaymentTx(currency);

            tx.Sign(Common.Alice, initTx.Outputs, currency.Network);

            Assert.True(tx.Verify(initTx.Outputs, currency.Network));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public BitcoinTransaction SignSegwitPaymentTx(BitcoinBasedConfig currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            var tx = CreateSegwitPaymentTx(currency);

            tx.Sign(Common.Alice, initTx.Outputs, currency.Network);

            Assert.True(tx.Verify(initTx.Outputs, currency.Network));

            return tx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public (BitcoinTransaction, byte[]) SignHtlcP2PkhScriptSwapPaymentTx(BitcoinBasedConfig currency)
        {
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, 1_0000_0000, 1_0000_0000);
            var (tx, redeemScript) = CreateHtlcP2PkhScriptSwapPaymentTx(currency);

            tx.Sign(Common.Alice, initTx.Outputs, currency.Network);

            Assert.True(tx.Verify(initTx.Outputs, currency.Network));

            return (tx, redeemScript);
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public BitcoinTransaction SignHtlcP2PkhScriptSwapRefundTx(BitcoinBasedConfig currency)
        {
            const int paymentQty = 1_0000_0000;

            var (paymentTx, redeemScriptBytes) = CreateHtlcP2PkhScriptSwapPaymentTx(currency);
            var paymentTxOutputs = paymentTx.Outputs.Where(o => o.Value == paymentQty).ToArray();

            var lockTime = Common.LockTime;
            const int amount = 9999_0000;
            const int fee = 1_0000;
            // change = 0;

            var redeemScript = new Script(redeemScriptBytes);

            var refundTx = BitcoinBasedCommon.CreatePaymentTx(
                currency: Common.BtcTestNet,
                outputs: paymentTxOutputs,
                from: Common.Alice.PubKey,
                to: Common.Alice.PubKey,
                amount: amount,
                fee: fee,
                lockTime: lockTime,
                knownRedeems: redeemScript
            );

            var sigHash = new uint256(refundTx.GetSignatureHash(paymentTxOutputs.First(), redeemScript));

            var aliceSign = Common.Alice.Sign(sigHash, new SigningOptions { SigHash = SigHash.All });

            var refundScript = BitcoinSwapTemplate.CreateSwapRefundScript(
                aliceRefundSig: aliceSign.ToBytes(),
                aliceRefundPubKey: Common.Alice.PubKey.ToBytes(),
                redeemScript: redeemScriptBytes);

            refundTx.SetSignature(refundScript, paymentTxOutputs.First());

            Assert.True(refundTx.Verify(paymentTxOutputs, currency.Network));

            return refundTx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public BitcoinTransaction SignHtlcP2PkhScriptSwapRedeemTx(BitcoinBasedConfig currency)
        {
            const int paymentQty = 1_0000_0000;

            var (paymentTx, redeemScriptBytes) = CreateHtlcP2PkhScriptSwapPaymentTx(currency);

            var paymentOutputs = paymentTx.Outputs
                .Where(o => o.Value == paymentQty)
                .ToArray();

            var paymentOutput = paymentOutputs.First();

            const int amount = 9999_0000;
            const int fee = 1_0000;

            var redeemScript = new Script(redeemScriptBytes);

            var redeemTx = currency.CreateTransaction(
                unspentOutputs: paymentOutputs,
                destinationAddress: Common.Bob.PubKey.GetAddress(ScriptPubKeyType.Legacy, currency.Network).ToString(),
                changeAddress: Common.Bob.PubKey.GetAddress(ScriptPubKeyType.Legacy, currency.Network).ToString(),
                amount: amount,
                fee: fee,
                lockTime: null,
                knownRedeems: new Script[] { redeemScript });

            var sigHash = new uint256(redeemTx.GetSignatureHash(paymentOutput, redeemScript));

            var bobSign = Common.Bob.Sign(sigHash, new SigningOptions { SigHash = SigHash.All });

            var scriptSig = BitcoinSwapTemplate.CreateSwapRedeemScript(
                sig: bobSign.ToBytes(),
                pubKey: Common.Bob.PubKey.ToBytes(),
                secret: Common.Secret,
                redeemScript: redeemScriptBytes);

            redeemTx.SetSignature(scriptSig, paymentOutput);

            Assert.True(redeemTx.Verify(paymentOutputs, currency.Network));

            return redeemTx;
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public void SpentSegwitPaymentTx(BitcoinBasedConfig currency)
        {
            const int paymentQty = 1_0000_0000;

            var paymentTx = SignSegwitPaymentTx(currency);
            var paymentTxOutputs = paymentTx.Outputs
                .Where(o => o.Value == paymentQty)
                .ToArray();

            var spentTx = currency.CreateTransaction(
                unspentOutputs: paymentTxOutputs,
                destinationAddress: Common.Bob
                    .PubKey
                    .GetAddress(ScriptPubKeyType.Segwit, currency.Network)
                    .ToString(),
                changeAddress: Common.Bob
                    .PubKey
                    .GetAddress(ScriptPubKeyType.Segwit, currency.Network)
                    .ToString(),
                amount: 9999_0000,
                fee: 1_0000);

            spentTx.Sign(Common.Bob, paymentTxOutputs, currency.Network);

            Assert.True(spentTx.Verify(paymentTxOutputs, currency.Network));
        }

        [Theory]
        [MemberData(nameof(Currencies))]
        public void ExtractSecretFromHtlcP2PkhScriptSwapRedeemTx(BitcoinBasedConfig currency)
        {
            var tx = SignHtlcP2PkhScriptSwapRedeemTx(currency);

            var data = tx.Inputs
                .First()
                .ExtractAllPushData();

            var secret = data.FirstOrDefault(d => d.SequenceEqual(Common.Secret));

            var secretHex = Common.Secret.ToHexString();

            Assert.NotNull(secret);
        }
    }
}