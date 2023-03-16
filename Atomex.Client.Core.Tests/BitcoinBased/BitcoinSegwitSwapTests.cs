using System;
using System.Linq;

using NBitcoin;
using Xunit;

using Atomex.Blockchain.Bitcoin;
using Atomex.Cryptography.Abstract;

namespace Atomex.Client.Core.Tests.BitcoinBased
{
    public class BitcoinSegwitSwapTests
    {
        public class SwapPaymentParams
        {
            public BitcoinTransaction Tx { get; set; }
            public Script LockScript { get; set; }
            public DateTimeOffset LockTime { get; set; }
            public byte[] Secret { get; set; }
        }

        [Fact]
        public SwapPaymentParams CreateSegwitSwapPayment()
        {
            var currency = Common.CurrenciesMainNet.Get<BitcoinConfig>("BTC");

            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, 1_0000_0000);

            var refundAddressSegwit = Common.AliceSegwitAddress(currency);
            var bobAddressSegwit = Common.BobSegwitAddress(currency);

            var lockTime = DateTimeOffset.UtcNow;
            var secret = new byte[32]; // zero secret
            var secretHash = HashAlgorithm.Sha256.Hash(secret, 2);

            var lockScript = BitcoinSwapTemplate.CreateHtlcSwapPayment(
                aliceRefundAddress: refundAddressSegwit,
                bobAddress: bobAddressSegwit,
                lockTimeStamp: lockTime.ToUnixTimeSeconds(),
                secretHash: secretHash,
                secretSize: 32,
                expectedNetwork: currency.Network);

            var lockScriptPubKeySegwit = lockScript.WitHash.ScriptPubKey;

            var tx = BitcoinTransaction.CreateTransaction(
                currency: currency.Name,
                coins: initTx.Outputs.Select(o => o.Coin),
                destination: lockScriptPubKeySegwit,
                change: Common.Alice.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                amount: 9999_0000,
                fee: 1_0000,
                network: currency.Network);

            tx.Sign(Common.Alice, initTx.Outputs[0], currency.Network);

            var verifyResult = tx.Verify(initTx.Outputs[0], currency.Network);

            Assert.True(verifyResult);

            return new SwapPaymentParams
            {
                Tx = tx,
                LockScript = lockScript,
                LockTime = lockTime,
                Secret = secret
            };
        }

        [Fact]
        public void CanRefundSegwitSwap()
        {
            var currency = Common.CurrenciesMainNet.Get<BitcoinConfig>("BTC");

            var swapPaymentParams = CreateSegwitSwapPayment();
            var swapOutput = swapPaymentParams.Tx.Outputs
                    .First(o => o.Coin.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2WSH));

            var tx = BitcoinTransaction.CreateTransaction(
                currency: currency.Name,
                coins: new Coin[] { swapOutput.Coin },
                destination: Common.Alice.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                change: Common.Alice.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                amount: 9998_0000,
                fee: 1_0000,
                network: currency.Network,
                lockTime: swapPaymentParams.LockTime,
                knownRedeems: new Script[] { swapPaymentParams.LockScript });

            var hash = tx.GetSignatureHash(swapOutput, swapPaymentParams.LockScript);

            var signature = Common.Alice
                .Sign(new uint256(hash.ToArray()), new SigningOptions(SigHash.All, useLowR: true))
                .ToBytes();

            var refundScriptSig = BitcoinSwapTemplate.CreateSwapRefundScript(
                aliceRefundSig: signature,
                aliceRefundPubKey: Common.Alice.PubKey.ToBytes(),
                redeemScript: swapPaymentParams.LockScript.ToBytes());

            var witRefundScriptSig = new WitScript(refundScriptSig);

            tx.SetSignature(witRefundScriptSig, swapOutput);

            var verifyResult = tx.Verify(swapOutput, out var errors, currency.Network);

            Assert.True(verifyResult);
        }

        [Fact]
        public void CannotRefundSegwitSwapFromOtherAddress()
        {
            var currency = Common.CurrenciesMainNet.Get<BitcoinConfig>("BTC");

            var swapPaymentParams = CreateSegwitSwapPayment();
            var swapOutput = swapPaymentParams.Tx.Outputs
                    .First(o => o.Coin.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2WSH));

            var tx = BitcoinTransaction.CreateTransaction(
                currency: currency.Name,
                coins: new Coin[] { swapOutput.Coin },
                destination: Common.Bob.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                change: Common.Bob.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                amount: 9998_0000,
                fee: 1_0000,
                network: currency.Network,
                lockTime: swapPaymentParams.LockTime,
                knownRedeems: new Script[] { swapPaymentParams.LockScript });

            var hash = tx.GetSignatureHash(swapOutput, swapPaymentParams.LockScript);

            var signature = Common.Bob
                .Sign(new uint256(hash.ToArray()), new SigningOptions(SigHash.All, useLowR: true))
                .ToBytes();

            foreach (var pubKey in new PubKey[] { Common.Alice.PubKey, Common.Bob.PubKey })
            { 
                var refundScriptSig = BitcoinSwapTemplate.CreateSwapRefundScript(
                    aliceRefundSig: signature,
                    aliceRefundPubKey: pubKey.ToBytes(),
                    redeemScript: swapPaymentParams.LockScript.ToBytes());

                var witRefundScriptSig = new WitScript(refundScriptSig);

                tx.SetSignature(witRefundScriptSig, swapOutput);

                var verifyResult = tx.Verify(swapOutput, out var errors, currency.Network);

                Assert.False(verifyResult);
            }
        }

        [Fact]
        public void CannotRefundSegwitSwapAheadOfRefundTime()
        {
            var currency = Common.CurrenciesMainNet.Get<BitcoinConfig>("BTC");

            var swapPaymentParams = CreateSegwitSwapPayment();
            var swapOutput = swapPaymentParams.Tx.Outputs
                    .First(o => o.Coin.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2WSH));

            var tx = BitcoinTransaction.CreateTransaction(
                currency: currency.Name,
                coins: new Coin[] { swapOutput.Coin },
                destination: Common.Alice.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                change: Common.Alice.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                amount: 9998_0000,
                fee: 1_0000,
                network: currency.Network,
                lockTime: swapPaymentParams.LockTime - TimeSpan.FromSeconds(1), // lockTime - 1sec
                knownRedeems: new Script[] { swapPaymentParams.LockScript });

            var hash = tx.GetSignatureHash(swapOutput, swapPaymentParams.LockScript);

            var signature = Common.Alice
                .Sign(new uint256(hash.ToArray()), new SigningOptions(SigHash.All, useLowR: true))
                .ToBytes();

            var refundScriptSig = BitcoinSwapTemplate.CreateSwapRefundScript(
                aliceRefundSig: signature,
                aliceRefundPubKey: Common.Alice.PubKey.ToBytes(),
                redeemScript: swapPaymentParams.LockScript.ToBytes());

            var witRefundScriptSig = new WitScript(refundScriptSig);

            tx.SetSignature(witRefundScriptSig, swapOutput);

            var verifyResult = tx.Verify(swapOutput, out var errors, currency.Network);

            Assert.False(verifyResult);
        }

        [Fact]
        public void CanRedeemSegwitSwap()
        {
            var currency = Common.CurrenciesMainNet.Get<BitcoinConfig>("BTC");

            var swapPaymentParams = CreateSegwitSwapPayment();
            var swapOutput = swapPaymentParams.Tx.Outputs
                .First(o => o.Coin.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2WSH));

            var tx = BitcoinTransaction.CreateTransaction(
                currency: currency.Name,
                coins: new Coin[] { swapOutput.Coin },
                destination: Common.Bob.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                change: Common.Bob.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                amount: 9998_0000,
                fee: 1_0000,
                network: currency.Network,
                knownRedeems: new Script[] { swapPaymentParams.LockScript });

            var hash = tx.GetSignatureHash(swapOutput, swapPaymentParams.LockScript);

            var signature = Common.Bob
                .Sign(new uint256(hash.ToArray()), new SigningOptions(SigHash.All, useLowR: true))
                .ToBytes();

            var redeemScriptSig = BitcoinSwapTemplate.CreateSwapRedeemScript(
                sig: signature,
                pubKey: Common.Bob.PubKey.ToBytes(),
                secret: swapPaymentParams.Secret,
                redeemScript: swapPaymentParams.LockScript.ToBytes());

            var witRedeemScriptSig = new WitScript(redeemScriptSig);

            tx.SetSignature(witRedeemScriptSig, swapOutput);

            var verifyResult = tx.Verify(swapOutput, out var errors, currency.Network);

            Assert.True(verifyResult);
        }

        [Fact]
        public void CannotRedeemSegwitSwapFromOtherAddress()
        {
            var currency = Common.CurrenciesMainNet.Get<BitcoinConfig>("BTC");

            var swapPaymentParams = CreateSegwitSwapPayment();
            var swapOutput = swapPaymentParams.Tx.Outputs
                .First(o => o.Coin.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2WSH));

            var tx = BitcoinTransaction.CreateTransaction(
                currency: currency.Name,
                coins: new Coin[] { swapOutput.Coin },
                destination: Common.Alice.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                change: Common.Alice.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                amount: 9998_0000,
                fee: 1_0000,
                network: currency.Network,
                knownRedeems: new Script[] { swapPaymentParams.LockScript });

            var hash = tx.GetSignatureHash(swapOutput, swapPaymentParams.LockScript);

            var signature = Common.Alice
                .Sign(new uint256(hash.ToArray()), new SigningOptions(SigHash.All, useLowR: true))
                .ToBytes();

            foreach (var pubKey in new PubKey[] { Common.Alice.PubKey, Common.Bob.PubKey })
            {
                var redeemScriptSig = BitcoinSwapTemplate.CreateSwapRedeemScript(
                    sig: signature,
                    pubKey: pubKey.ToBytes(),
                    secret: swapPaymentParams.Secret,
                    redeemScript: swapPaymentParams.LockScript.ToBytes());

                var witRedeemScriptSig = new WitScript(redeemScriptSig);

                tx.SetSignature(witRedeemScriptSig, swapOutput);

                var verifyResult = tx.Verify(swapOutput, out var errors, currency.Network);

                Assert.False(verifyResult);
            }
        }

        [Fact]
        public void CannotRedeemSegwitSwapWithInvalidSecret()
        {
            var currency = Common.CurrenciesMainNet.Get<BitcoinConfig>("BTC");

            var swapPaymentParams = CreateSegwitSwapPayment();
            var swapOutput = swapPaymentParams.Tx.Outputs
                .First(o => o.Coin.TxOut.ScriptPubKey.IsScriptType(ScriptType.P2WSH));

            var tx = BitcoinTransaction.CreateTransaction(
                currency: currency.Name,
                coins: new Coin[] { swapOutput.Coin },
                destination: Common.Bob.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                change: Common.Bob.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit),
                amount: 9998_0000,
                fee: 1_0000,
                network: currency.Network,
                knownRedeems: new Script[] { swapPaymentParams.LockScript });

            var hash = tx.GetSignatureHash(swapOutput, swapPaymentParams.LockScript);

            var signature = Common.Bob
                .Sign(new uint256(hash.ToArray()), new SigningOptions(SigHash.All, useLowR: true))
                .ToBytes();

            var badSecret = new byte[32];
            badSecret[0] = 0xFF;

            var redeemScriptSig = BitcoinSwapTemplate.CreateSwapRedeemScript(
                sig: signature,
                pubKey: Common.Bob.PubKey.ToBytes(),
                secret: badSecret, // try bad secret
                redeemScript: swapPaymentParams.LockScript.ToBytes());

            var witRedeemScriptSig = new WitScript(redeemScriptSig);

            tx.SetSignature(witRedeemScriptSig, swapOutput);

            var verifyResult = tx.Verify(swapOutput, out var errors, currency.Network);

            Assert.False(verifyResult);
        }
    }
}