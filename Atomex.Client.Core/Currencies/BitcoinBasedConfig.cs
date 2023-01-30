using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;

using Atomex.Blockchain.Bitcoin;
using Atomex.Core;
using Atomex.Common.Memory;
using Atomex.Wallets;
using Atomex.Wallets.Bitcoin;
using BitcoinExtKey = Atomex.Wallets.Bitcoin.BitcoinExtKey;

namespace Atomex
{
    public abstract class BitcoinBasedConfig : CurrencyConfig
    {
        public const int P2PkhTxSize = 182;
        public const int DefaultPaymentTxSize = 372; // 2 inputs and 2 outputs
        public const int DefaultRedeemTxSize = 300;
        public const int OneInputTwoOutputTxSize = 226; // size for legacy transaction with one P2PKH input and two P2PKH outputs
        public const int LegacyTxOutputSize = 34;

        public decimal FeeRate { get; set; }
        public decimal DustFeeRate { get; set; }
        public decimal MinTxFeeRate { get; set; }
        public decimal MinRelayTxFeeRate { get; set; }
        public NBitcoin.Network Network { get; protected set; }

        protected BitcoinBasedConfig()
        {
            TransactionType = typeof(BitcoinTransaction);
        }

        public override IExtKey CreateExtKey(SecureBytes seed, int keyType) => 
            CreateExtKeyFromSeed(seed);

        public static IExtKey CreateExtKeyFromSeed(SecureBytes seed) => 
            new BitcoinExtKey(seed);

        public override IKey CreateKey(SecureBytes seed) => 
            new BitcoinKey(seed);

        public override string AddressFromKey(byte[] publicKey) =>
            new PubKey(publicKey)
                .GetAddress(ScriptPubKeyType.Legacy, Network)
                .ToString();

        public override bool IsValidAddress(string address)
        {
            try
            {
                BitcoinAddress.Create(address, Network);
            }
            catch (FormatException)
            {
                return false;
            }

            return true;
        }

        public override async Task<decimal> GetPaymentFeeAsync(
            CancellationToken cancellationToken = default)
        {
            var feeRate = await GetFeeRateAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return feeRate * DefaultPaymentTxSize / DigitsMultiplier;
        }

        public override async Task<BigInteger> GetRedeemFeeAsync(
            WalletAddress toAddress = null,
            CancellationToken cancellationToken = default)
        {
            var feeRate = await GetFeeRateAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new BigInteger(feeRate * DefaultRedeemTxSize);
        }

        public override Task<BigInteger> GetEstimatedRedeemFeeAsync(
            WalletAddress toAddress = null,
            bool withRewardForRedeem = false,
            CancellationToken cancellationToken = default) =>
            GetRedeemFeeAsync(toAddress, cancellationToken);

        public override Task<decimal> GetRewardForRedeemAsync(
            decimal maxRewardPercent,
            decimal maxRewardPercentInBase,
            string feeCurrencyToBaseSymbol,
            decimal feeCurrencyToBasePrice,
            string feeCurrencySymbol = null,
            decimal feeCurrencyPrice = 0,
            CancellationToken cancellationToken = default) => Task.FromResult(0m);

        public long GetMinimumFee(int txSize) =>
            (long) (MinTxFeeRate * txSize);

        public long GetMinimumRelayFee(int txSize) =>
            (long) (MinRelayTxFeeRate * txSize);

        public virtual long GetDust() =>
            (long) (DustFeeRate * P2PkhTxSize);

        public long CoinToSatoshi(decimal coins) =>
            (long) (coins * DigitsMultiplier);

        public decimal SatoshiToCoin(long satoshi) =>
            satoshi / DigitsMultiplier;

        public string TestAddress()
        {
            return new Key()
                .PubKey
                .GetAddress(ScriptPubKeyType.Legacy, Network)
                .ToString();
        }

        public BitcoinTransaction CreatePaymentTx(
            IEnumerable<BitcoinTxOutput> unspentOutputs,
            string destinationAddress,
            string changeAddress,
            long amount,
            long fee,
            DateTimeOffset lockTime,
            params Script[] knownRedeems)
        {
            return CreateP2PkhTx(
                unspentOutputs: unspentOutputs,
                destinationAddress: destinationAddress,
                changeAddress: changeAddress,
                amount: amount,
                fee: fee,
                lockTime: lockTime,
                knownRedeems: knownRedeems);
        }

        public BitcoinTransaction CreateP2PkhTx(
            IEnumerable<BitcoinTxOutput> unspentOutputs,
            string destinationAddress,
            string changeAddress,
            long amount,
            long fee,
            DateTimeOffset lockTime,
            params Script[] knownRedeems)
        {
            var coins = unspentOutputs
                .Select(o => o.Coin);

            var destination = BitcoinAddress.Create(destinationAddress, Network)
                .ScriptPubKey;

            var change = BitcoinAddress.Create(changeAddress, Network)
                .ScriptPubKey;

            return BitcoinTransaction.CreateTransaction(
                currency: Name,
                coins: coins,
                destination: destination,
                change: change,
                amount: amount,
                fee: fee,
                lockTime: lockTime,
                network: Network,
                knownRedeems: knownRedeems);
        }

        public BitcoinTransaction CreateP2WPkhTx(
            IEnumerable<BitcoinTxOutput> unspentOutputs,
            string destinationAddress,
            string changeAddress,
            long amount,
            long fee,
            params Script[] knownRedeems)
        {
            var coins = unspentOutputs
                .Select(o => o.Coin);

            var destination = new BitcoinWitPubKeyAddress(destinationAddress, Network)
                .ScriptPubKey;

            var change = new BitcoinWitPubKeyAddress(changeAddress, Network)
                .ScriptPubKey;

            return BitcoinTransaction.CreateTransaction(
                currency: Name,
                coins: coins,
                destination: destination,
                change: change,
                amount: amount,
                fee: fee,
                network: Network,
                knownRedeems: knownRedeems);
        }

        public virtual BitcoinTransaction CreateHtlcP2PkhScriptSwapPaymentTx(
            IEnumerable<BitcoinTxOutput> unspentOutputs,
            string aliceRefundAddress,
            string bobAddress,
            DateTimeOffset lockTime,
            byte[] secretHash,
            int secretSize,
            long amount,
            long fee,
            out byte[] redeemScript)
        {
            var coins = unspentOutputs
                .Select(o => o.Coin);

            var swap = BitcoinSwapTemplate.GenerateHtlcP2PkhSwapPayment(
                aliceRefundAddress: aliceRefundAddress,
                bobAddress: bobAddress,
                lockTimeStamp: lockTime.ToUnixTimeSeconds(),
                secretHash: secretHash,
                secretSize: secretSize,
                expectedNetwork: Network);

            redeemScript = swap.ToBytes();

            var change = BitcoinAddress.Create(aliceRefundAddress, Network)
                .ScriptPubKey;

            return BitcoinTransaction.CreateTransaction(
                currency: Name,
                coins: coins,
                destination: swap.PaymentScript,
                change: change,
                amount: amount,
                fee: fee,
                network: Network);
        }

        public virtual Task<decimal> GetFeeRateAsync(
            bool useCache = true,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FeeRate);
    }
}