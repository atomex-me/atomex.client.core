using System;
using System.Collections.Generic;
using System.Linq;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography;
using Atomex.Wallet.BitcoinBased;
using NBitcoin;
using Serilog;

namespace Atomex
{
    public abstract class BitcoinBasedCurrency : Currency
    {
        public const int P2PkhTxSize = 182;
        public const int P2PkhScriptSigSize = 139; //1 + 72 + 1 + 65;
        public const int P2PkhCompressedScriptSigSize = 107; // 1 + 72 + 1 + 33
        public const int P2PkhSwapRefundSigSize = 146; //1 + 72 + 72 + 1
        public const int P2PkhSwapRedeemSigSize = 82; //65 + 16 + 1;
        public const int P2WPkhScriptSigSize = P2PkhScriptSigSize / 4;

        public const int P2PShSwapRefundScriptSigSize = 208;
        public const int P2PShSwapRedeemScriptSigSize = 241;

        public const int DefaultRedeemTxSize = 300;
        public const int OutputSize = 34;

        public decimal FeeRate { get; set; }
        public decimal DustFeeRate { get; set; }
        public decimal MinTxFeeRate { get; set; }
        public decimal MinRelayTxFeeRate { get; set; }
        public NBitcoin.Network Network { get; protected set; }

        protected BitcoinBasedCurrency()
        {
            TransactionType = typeof(BitcoinBasedTransaction);
        }

        public override IExtKey CreateExtKey(SecureBytes seed)
        {
            return CreateExtKeyFromSeed(seed);
        }

        public static IExtKey CreateExtKeyFromSeed(SecureBytes seed)
        {
            return new BitcoinBasedExtKey(seed);
        }

        public override IKey CreateKey(SecureBytes seed)
        {
            return new BitcoinBasedKey(seed);
        }

        public override string AddressFromKey(byte[] publicKey)
        {
            return new PubKey(publicKey)
                .ToString(Network);
        }

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

        public override bool IsAddressFromKey(string address, byte[] publicKey)
        {
            try
            {
                return new PubKey(publicKey)
                    .GetAddress(ScriptPubKeyType.Legacy, Network)
                    .ToString()
                    .Equals(address);
            }
            catch (Exception)
            {
                return false;
            } 
        }

        public override bool VerifyMessage(byte[] data, byte[] signature, byte[] publicKey)
        {
            return new PubKey(publicKey)
                .VerifyMessage(data, Convert.ToBase64String(signature));
        }

        public override decimal GetFeeAmount(decimal fee, decimal feePrice)
        {
            return fee;
        }

        public override decimal GetFeeFromFeeAmount(decimal feeAmount, decimal feePrice)
        {
            return feeAmount;
        }

        public override decimal GetFeePriceFromFeeAmount(decimal feeAmount, decimal fee)
        {
            return 1m;
        }

        public override decimal GetRedeemFee(WalletAddress toAddress = null)
        {
            return FeeRate * DefaultRedeemTxSize / DigitsMultiplier;
        }

        public override decimal GetRewardForRedeem()
        {
            return 0;
        }

        public long GetMinimumFee(int txSize)
        {
            return (long)(MinTxFeeRate * txSize);
        }
        public long GetMinimumRelayFee(int txSize)
        {
            return (long)(MinRelayTxFeeRate * txSize);
        }

        public virtual long GetDust()
        {
            return (long) (DustFeeRate * P2PkhTxSize);
        }

        public long CoinToSatoshi(decimal coins)
        {
            return (long) (coins * DigitsMultiplier);
        }

        public decimal SatoshiToCoin(long satoshi)
        {
            return satoshi / (decimal)DigitsMultiplier;
        }

        public string TestAddress()
        {
            return new Key()
                .PubKey
                .GetAddress(ScriptPubKeyType.Legacy, Network)
                .ToString();
        }

        public IBitcoinBasedTransaction CreatePaymentTx(
            IEnumerable<ITxOutput> unspentOutputs,
            string destinationAddress,
            string changeAddress,
            long amount,
            long fee,
            DateTimeOffset lockTime)
        {
            return CreateP2PkhTx(
                unspentOutputs: unspentOutputs,
                destinationAddress: destinationAddress,
                changeAddress: changeAddress,
                amount: amount,
                fee: fee,
                lockTime: lockTime);
        }

        public IBitcoinBasedTransaction CreateP2PkhTx(
            IEnumerable<ITxOutput> unspentOutputs,
            string destinationAddress,
            string changeAddress,
            long amount,
            long fee,
            DateTimeOffset lockTime)
        {
            var coins = unspentOutputs
                .Cast<BitcoinBasedTxOutput>()
                .Select(o => o.Coin);

            var destination = BitcoinAddress.Create(destinationAddress, Network)
                .ScriptPubKey;

            var change = BitcoinAddress.Create(changeAddress, Network)
                .ScriptPubKey;

            return BitcoinBasedTransaction.CreateTransaction(
                currency: this,
                coins: coins,
                destination: destination,
                change: change,
                amount: amount,
                fee: fee,
                lockTime: lockTime);
        }

        public IBitcoinBasedTransaction CreateP2WPkhTx(
            IEnumerable<ITxOutput> unspentOutputs,
            string destinationAddress,
            string changeAddress,
            long amount,
            long fee)
        {
            var coins = unspentOutputs
                .Cast<BitcoinBasedTxOutput>()
                .Select(o => o.Coin);

            var destination = new BitcoinWitPubKeyAddress(destinationAddress, Network)
                .ScriptPubKey;

            var change = new BitcoinWitPubKeyAddress(changeAddress, Network)
                .ScriptPubKey;

            return BitcoinBasedTransaction.CreateTransaction(
                currency: this,
                coins: coins,
                destination: destination,
                change: change,
                amount: amount,
                fee: fee);
        }

        public virtual IBitcoinBasedTransaction CreateP2PkSwapPaymentTx(
            IEnumerable<ITxOutput> unspentOutputs,
            byte[] aliceRefundPubKey,
            byte[] bobRefundPubKey,
            byte[] bobDestinationPubKey,
            byte[] secretHash,
            long amount,
            long fee)
        {
            var coins = unspentOutputs
                .Cast<BitcoinBasedTxOutput>()
                .Select(o => o.Coin);

            var alicePubKey = new PubKey(aliceRefundPubKey);

            var swap = BitcoinBasedSwapTemplate.GenerateP2PkSwapPayment(
                aliceRefundPubKey: aliceRefundPubKey,
                bobRefundPubKey: bobRefundPubKey,
                bobDestinationPubKey: bobDestinationPubKey,
                secretHash: secretHash);

            return BitcoinBasedTransaction.CreateTransaction(
                currency: this,
                coins: coins,
                destination: swap,
                change: alicePubKey.Hash.ScriptPubKey,
                amount: amount,
                fee: fee);
        }

        public virtual IBitcoinBasedTransaction CreateP2PkhSwapPaymentTx(
            IEnumerable<ITxOutput> unspentOutputs,
            byte[] aliceRefundPubKey,
            byte[] bobRefundPubKey,
            string bobAddress,
            byte[] secretHash,
            long amount,
            long fee)
        {
            var coins = unspentOutputs
                .Cast<BitcoinBasedTxOutput>()
                .Select(o => o.Coin);

            var alicePubKey = new PubKey(aliceRefundPubKey);

            var swap = BitcoinBasedSwapTemplate.GenerateP2PkhSwapPayment(
                aliceRefundPubKey: aliceRefundPubKey,
                bobRefundPubKey: bobRefundPubKey,
                bobAddress: bobAddress,
                secretHash: secretHash,
                expectedNetwork: Network);

            return BitcoinBasedTransaction.CreateTransaction(
                currency: this,
                coins: coins,
                destination: swap,
                change: alicePubKey.Hash.ScriptPubKey,
                amount: amount,
                fee: fee);
        }

        public virtual IBitcoinBasedTransaction CreateHtlcP2PkhSwapPaymentTx(
            IEnumerable<ITxOutput> unspentOutputs,
            string aliceRefundAddress,
            string bobAddress,
            DateTimeOffset lockTime,
            byte[] secretHash,
            int secretSize,
            long amount,
            long fee)
        {
            var coins = unspentOutputs
                .Cast<BitcoinBasedTxOutput>()
                .Select(o => o.Coin);

            var swap = BitcoinBasedSwapTemplate.GenerateHtlcP2PkhSwapPayment(
                aliceRefundAddress: aliceRefundAddress,
                bobAddress: bobAddress,
                lockTimeStamp: lockTime.ToUnixTimeSeconds(),
                secretHash: secretHash,
                secretSize: secretSize,
                expectedNetwork: Network);

            var change = BitcoinAddress.Create(aliceRefundAddress, Network)
                .ScriptPubKey;

            return BitcoinBasedTransaction.CreateTransaction(
                currency: this,
                coins: coins,
                destination: swap,
                change: change,
                amount: amount,
                fee: fee);
        }

        public virtual IBitcoinBasedTransaction CreateHtlcP2PkhScriptSwapPaymentTx(
            IEnumerable<ITxOutput> unspentOutputs,
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
                .Cast<BitcoinBasedTxOutput>()
                .Select(o => o.Coin);

            var swap = BitcoinBasedSwapTemplate.GenerateHtlcP2PkhSwapPayment(
                aliceRefundAddress: aliceRefundAddress,
                bobAddress: bobAddress,
                lockTimeStamp: lockTime.ToUnixTimeSeconds(),
                secretHash: secretHash,
                secretSize: secretSize,
                expectedNetwork: Network);

            redeemScript = swap.ToBytes();

            var change = BitcoinAddress.Create(aliceRefundAddress, Network)
                .ScriptPubKey;

            return BitcoinBasedTransaction.CreateTransaction(
                currency: this,
                coins: coins,
                destination: swap.PaymentScript,
                change: change,
                amount: amount,
                fee: fee);
        }

        public static long EstimateSigSize(ITxOutput output, bool forRefund = false, bool forRedeem = false)
        {
            if (!(output is BitcoinBasedTxOutput btcBasedOutput))
                return 0;

            var sigSize = 0L;

            if (btcBasedOutput.IsP2Pkh)
                sigSize += P2PkhScriptSigSize; // use compressed?
            else if (btcBasedOutput.IsSegwitP2Pkh)
                sigSize += P2WPkhScriptSigSize;
            else if (btcBasedOutput.IsP2PkhSwapPayment || btcBasedOutput.IsHtlcP2PkhSwapPayment)
                sigSize += forRefund
                    ? P2PkhSwapRefundSigSize
                    : P2PkhSwapRedeemSigSize;
            else if (btcBasedOutput.IsP2Sh)
                sigSize += forRefund
                    ? P2PShSwapRefundScriptSigSize
                    : (forRedeem
                        ? P2PShSwapRedeemScriptSigSize
                        : P2PkhScriptSigSize); // todo: probably incorrect
            else
                Log.Warning("Unknown output type, estimated fee may be wrong");

            return sigSize;
        }

        public static long EstimateSigSize(IEnumerable<ITxOutput> outputs, bool forRefund = false)
        {
            return outputs.ToList().Sum(output => EstimateSigSize(output, forRefund));
        }
    }
}