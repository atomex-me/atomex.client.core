using Atomex.Common;
using System;
using System.Collections.Generic;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Core;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class BitcoinBasedSwapTransactionVerifierTests
    {
        public static IEnumerable<object[]> Currencies =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesMainNet.Get<Bitcoin>(), Common.SymbolsMainNet.GetByName("ETH/BTC"), Side.Sell},
                new object[] {Common.CurrenciesTestNet.Get<Bitcoin>(), Common.SymbolsTestNet.GetByName("ETH/BTC"), Side.Sell},
                new object[] {Common.CurrenciesMainNet.Get<Litecoin>(), Common.SymbolsMainNet.GetByName("LTC/BTC"), Side.Buy},
                new object[] {Common.CurrenciesTestNet.Get<Litecoin>(), Common.SymbolsTestNet.GetByName("LTC/BTC"), Side.Buy}
            };

        [Theory]
        [MemberData(nameof(Currencies))]
        public void TryVerifyPartyPaymentTxTest(BitcoinBasedCurrency currency, Symbol symbol, Side side)
        {
            const int initAmount = 1_0000_0000;
            var initTx = BitcoinBasedCommon.CreateFakeTx(currency, Common.Alice.PubKey, initAmount);

            var refundLockTimeInSec = 60 * 60;
            var utcNow = DateTimeOffset.UtcNow;
            var lockTime = utcNow.AddSeconds(refundLockTimeInSec);
           
            var tx = currency.CreateHtlcP2PkhScriptSwapPaymentTx(
                unspentOutputs: initTx.Outputs,
                aliceRefundAddress: Common.Alice.PubKey.GetAddress(currency),
                bobAddress: Common.Bob.PubKey.GetAddress(currency),
                lockTime: lockTime,
                secretHash: Common.SecretHash,
                secretSize: Common.Secret.Length,
                amount: 9999_0000,
                fee: 1_0000,
                redeemScript: out var redeemScript);

            tx.Sign(Common.Alice, initTx.Outputs);

            var swap = new Swap
            {
                PartyRedeemScript = Convert.ToBase64String(redeemScript),
                ToAddress = Common.Bob.PubKey.GetAddress(currency),
                TimeStamp = utcNow.UtcDateTime,
                Symbol = symbol,
                Side = side,
                Price = 1,
                Qty = 0.9999m
            };

            var result = BitcoinBasedTransactionVerifier.TryVerifyPartyPaymentTx(
                tx: tx,
                swap: swap,
                secretHash: Common.SecretHash,
                refundLockTime: refundLockTimeInSec,
                error: out var errors);

            Assert.True(result);
            Assert.Null(errors);
        }
    }
}