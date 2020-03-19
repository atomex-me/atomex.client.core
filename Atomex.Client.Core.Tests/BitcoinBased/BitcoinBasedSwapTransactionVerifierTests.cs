using Atomex.Common;
using System;
using System.Collections.Generic;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Core;
using Xunit;
using Atomex.Abstract;

namespace Atomex.Client.Core.Tests
{
    public class BitcoinBasedSwapTransactionVerifierTests
    {
        public static IEnumerable<object[]> Currencies =>
            new List<object[]>
            {
                new object[] {Common.CurrenciesMainNet, "BTC", "ETH/BTC", Side.Sell},
                new object[] {Common.CurrenciesTestNet, "BTC", "ETH/BTC", Side.Sell},
                new object[] {Common.CurrenciesMainNet, "LTC", "LTC/BTC", Side.Buy},
                new object[] {Common.CurrenciesTestNet, "LTC", "LTC/BTC", Side.Buy}
            };

        [Theory]
        [MemberData(nameof(Currencies))]
        public void TryVerifyPartyPaymentTxTest(
            ICurrencies currencies,
            string currency,
            string symbol,
            Side side)
        {
            var currencyConfig = currencies.Get<BitcoinBasedCurrency>(currency);

            const int initAmount = 1_0000_0000;
            var initTx = BitcoinBasedCommon.CreateFakeTx(currencyConfig, Common.Alice.PubKey, initAmount);

            var refundLockTimeInSec = 60 * 60;
            var utcNow = DateTimeOffset.UtcNow;
            var lockTime = utcNow.AddSeconds(refundLockTimeInSec);
           
            var tx = currencyConfig.CreateHtlcP2PkhScriptSwapPaymentTx(
                unspentOutputs: initTx.Outputs,
                aliceRefundAddress: Common.Alice.PubKey.GetAddress(currencyConfig),
                bobAddress: Common.Bob.PubKey.GetAddress(currencyConfig),
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
                ToAddress = Common.Bob.PubKey.GetAddress(currencyConfig),
                TimeStamp = utcNow.UtcDateTime,
                Symbol = symbol,
                Side = side,
                Price = 1,
                Qty = 0.9999m
            };

            var result = BitcoinBasedTransactionVerifier.TryVerifyPartyPaymentTx(
                tx: tx,
                swap: swap,
                currencies: currencies,
                secretHash: Common.SecretHash,
                refundLockTime: refundLockTimeInSec,
                error: out var errors);

            Assert.True(result);
            Assert.Null(errors);
        }
    }
}