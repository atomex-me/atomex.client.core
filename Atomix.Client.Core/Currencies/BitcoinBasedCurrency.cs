using System;
using System.Collections.Generic;
using System.Linq;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Core.Entities;
using Atomix.Cryptography;
using Atomix.Wallet.BitcoinBased;
using NBitcoin;

namespace Atomix
{
    public abstract class BitcoinBasedCurrency : Currency
    {
        public const int P2PkhScriptSigSize = 139; //1 + 72 + 1 + 65;
        public const int P2PkhCompressedScriptSigSize = 107; // 1 + 72 + 1 + 33
        public const int P2PkhSwapRefundSigSize = 146; //1 + 72 + 72 + 1
        public const int P2PkhSwapRedeemSigSize = 82; //65 + 16 + 1;
        public const int P2WPkhScriptSigSize = P2PkhScriptSigSize / 4;
        //public const int P2WPkhCompressedScriptSigSize = P2PkhCompressedScriptSigSize / 4;
        public const int RedeemTxSize = 225;

        public Network Network { get; protected set; }

        protected BitcoinBasedCurrency()
        {
            TransactionType = typeof(BitcoinBasedTransaction);
        }

        public override IExtKey CreateExtKey(byte[] seed)
        {
            return CreateExtKeyFromSeed(seed);
        }

        public static IExtKey CreateExtKeyFromSeed(byte[] seed)
        {
            return new BitcoinBasedExtKey(seed);
        }

        public override IKey CreateKey(byte[] seed)
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

        public override decimal GetDefaultRedeemFee()
        {
            return FeeRate * RedeemTxSize / DigitsMultiplier;
        }

        public long CoinToSatoshi(decimal coins)
        {
            return (long) (coins * DigitsMultiplier);
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
            long fee)
        {
            return CreateP2PkhTx(
                unspentOutputs: unspentOutputs,
                destinationAddress: destinationAddress,
                changeAddress: changeAddress,
                amount: amount,
                fee: fee);
        }

        public IBitcoinBasedTransaction CreateP2PkhTx(
            IEnumerable<ITxOutput> unspentOutputs,
            string destinationAddress,
            string changeAddress,
            long amount,
            long fee)
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
                fee: fee);
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

        public virtual IBitcoinBasedTransaction CreateSwapRefundTx(
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
    }
}