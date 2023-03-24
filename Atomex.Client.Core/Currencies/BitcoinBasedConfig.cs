using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;

using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Blockchain.Bitcoin.SoChain;
using Atomex.Blockchain.BlockCypher;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Wallets;
using Atomex.Wallets.Bitcoin;
using Atomex.Wallets.Bips;
using BitcoinExtKey = Atomex.Wallets.Bitcoin.BitcoinExtKey;
using Atomex.Wallets.Abstract;

namespace Atomex
{
    public abstract class BitcoinBasedConfig : CurrencyConfig
    {
        public const int P2PkhTxSize = 182;
        public const int DefaultPaymentTxSize = 372; // 2 inputs and 2 outputs
        public const int DefaultRedeemTxSize = 300;
        public const int OneInputTwoOutputTxSize = 226; // size for legacy transaction with one P2PKH input and two P2PKH outputs
        public const int LegacyTxOutputSize = 34;
        public const int SegwitKey = 1;

        public decimal FeeRate { get; set; }
        public decimal DustFeeRate { get; set; }
        public decimal MinTxFeeRate { get; set; }
        public decimal MinRelayTxFeeRate { get; set; }
        public NBitcoin.Network Network { get; protected set; }
        public SoChainSettings SoChainSettings { get; protected set; }
        public BlockCypherSettings BlockCypherSettings { get; protected set; }

        protected BitcoinBasedConfig()
        {
            TransactionType = typeof(BitcoinTransaction);
            TransactionMetadataType = typeof(TransactionMetadata);
        }

        public override IBlockchainApi GetBlockchainApi() => GetBitcoinBlockchainApi();
        public abstract BitcoinBlockchainApi GetBitcoinBlockchainApi();

        public override IExtKey CreateExtKey(SecureBytes seed, int keyType) => 
            CreateExtKeyFromSeed(seed);
        public static IExtKey CreateExtKeyFromSeed(SecureBytes seed) => 
            new BitcoinExtKey(seed);
        public override IKey CreateKey(SecureBytes seed) => 
            new BitcoinKey(seed);
        public override string AddressFromKey(byte[] publicKey, int keyType) =>
            new PubKey(publicKey)
                .GetAddress(
                    type: keyType == SegwitKey
                        ? ScriptPubKeyType.Segwit
                        : ScriptPubKeyType.Legacy,
                    network: Network)
                .ToString();

        public override bool IsValidAddress(string address)
        {
            try
            {
                var addr = BitcoinAddress.Create(address, Network);

                return addr != null;
            }
            catch
            {
                // format error
            }

            return false;
        }

        public override string GetKeyPathPattern(int keyType) =>
            keyType switch
            {
                SegwitKey => $"m/{Bip84.Purpose}'/{Bip44Code}'/{{a}}'/{{c}}/{{i}}",
                StandardKey or _ => base.GetKeyPathPattern(keyType),
            };

        public override async Task<Result<BigInteger>> GetPaymentFeeAsync(
            CancellationToken cancellationToken = default)
        {
            var feeRate = await GetFeeRateAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new BigInteger(feeRate * DefaultPaymentTxSize);
        }

        public override async Task<Result<BigInteger>> GetRedeemFeeAsync(
            WalletAddress toAddress = null,
            CancellationToken cancellationToken = default)
        {
            var feeRate = await GetFeeRateAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new BigInteger(feeRate * DefaultRedeemTxSize);
        }

        public override Task<Result<BigInteger>> GetEstimatedRedeemFeeAsync(
            WalletAddress toAddress = null,
            bool withRewardForRedeem = false,
            CancellationToken cancellationToken = default) =>
            GetRedeemFeeAsync(toAddress, cancellationToken);

        public override Task<Result<decimal>> GetRewardForRedeemAsync(
            decimal maxRewardPercent,
            decimal maxRewardPercentInBase,
            string feeCurrencyToBaseSymbol,
            decimal feeCurrencyToBasePrice,
            string feeCurrencySymbol = null,
            decimal feeCurrencyPrice = 0,
            CancellationToken cancellationToken = default) => Task.FromResult(new Result<decimal> { Value = 0m });

        public long GetMinimumFee(int txSize) =>
            (long) (MinTxFeeRate * txSize);

        public virtual long GetDust() =>
            (long) (DustFeeRate * P2PkhTxSize);

        public BigInteger CoinToSatoshi(decimal coins) =>
            coins.ToBigInteger(Decimals);

        public decimal SatoshiToCoin(BigInteger satoshi) =>
            satoshi.ToDecimal(Decimals);

        public BitcoinTransaction CreateTransaction(
            IEnumerable<BitcoinTxOutput> unspentOutputs,
            string destinationAddress,
            string changeAddress,
            long amount,
            long fee,
            DateTimeOffset? lockTime = null,
            Script[]? knownRedeems = null)
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
                network: Network,
                lockTime: lockTime,
                knownRedeems: knownRedeems);
        }

        public virtual BitcoinTransaction CreateHtlcSegwitScriptSwapPaymentTx(
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

            var lockScript = BitcoinSwapTemplate.CreateHtlcSwapPayment(
                aliceRefundAddress: aliceRefundAddress,
                bobAddress: bobAddress,
                lockTimeStamp: lockTime.ToUnixTimeSeconds(),
                secretHash: secretHash,
                secretSize: secretSize,
                expectedNetwork: Network);

            redeemScript = lockScript.ToBytes();

            var change = BitcoinAddress.Create(aliceRefundAddress, Network)
                .ScriptPubKey;

            var destinationSegwit = lockScript.WitHash.ScriptPubKey;

            return BitcoinTransaction.CreateTransaction(
                currency: Name,
                coins: coins,
                destination: destinationSegwit,
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