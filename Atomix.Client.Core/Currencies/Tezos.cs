using System;
using Atomix.Blockchain.Tezos;
using Atomix.Blockchain.Tezos.Internal;
using Atomix.Core.Entities;
using Atomix.Cryptography;
using Atomix.Wallet.Bip;

namespace Atomix
{
    public class Tezos : Currency
    {
        public const long XtzDigitsMultiplier = 1_000_000;
        public const string AlphanetSwapContractAddress = "KT1FU74GimCeEVRAEZGURb6TWU8jK1N6zFJy";
        public const long DefaultFee = 1300;
        public const long DefaultGasLimit = 20000;
        public const long DefaultStorageLimit = 20000;
        public const long DefaultPaymentFee = 50000;
        public const long DefaultPaymentGasLimit = 400000;
        public const long DefaultPaymentStorageLimit = 60000;
        public const long DefaultRedeemFee = 50000;
        public const long DefaultRedeemGasLimit = 400000;
        public const long DefaultRedeemStorageLimit = 60000;
        public const long DefaultRefundFee = 50000;
        public const long DefaultRefundGasLimit = 400000;
        public const long DefaultRefundStorageLimit = 60000;

        private const int PkHashSize = 20 * 8;

        public TezosNetwork Network { get; set; }
        public string RpcProvider { get; set; }
        public string SwapContractAddress { get; protected set; }

        public Tezos()
        {
            Name = "XTZ";
            Description = "Tezos";
            DigitsMultiplier = XtzDigitsMultiplier;
            Digits = (int)Math.Log10(XtzDigitsMultiplier);
            Format = $"F{Digits}";

            //FeeRate = 0; 
            FeeDigits = Digits;
            FeeCode = Name;
            FeeFormat = $"F{FeeDigits}";

            HasFeePrice = false;

            Network = TezosNetwork.Alphanet;
            RpcProvider = TzScanApi.RpcByNetwork(Network);
            BlockchainApi = new TzScanApi(Network);
            SwapContractAddress = AlphanetSwapContractAddress;
            TransactionType = typeof(TezosTransaction);

            IsTransactionsAvailable = true;
            IsSwapAvailable = true;
            Bip44Code = Bip44.Tezos;
        }

        public override string AddressFromKey(byte[] publicKey)
        {
            return Base58Check.Encode(
                payload: new HmacBlake2b(PkHashSize).ComputeHash(publicKey),
                prefix: Prefix.Tz1);
        }

        public override bool IsValidAddress(string address)
        {
            return Address.CheckTz1Address(address) ||
                   Address.CheckTz2Address(address) ||
                   Address.CheckTz3Address(address) ||
                   Address.CheckKtAddress(address);
        }

        public override bool IsAddressFromKey(string address, byte[] publicKey)
        {
            return AddressFromKey(publicKey).ToLowerInvariant()
                .Equals(address.ToLowerInvariant());
        }

        public override bool VerifyMessage(byte[] data, byte[] signature, byte[] publicKey)
        {
            return new TezosSigner()
                .Verify(
                    data: data,
                    signature: signature,
                    publicKey: publicKey);
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

        public static decimal MtzToTz(decimal mtz)
        {
            return mtz / XtzDigitsMultiplier;
        }
    }
}