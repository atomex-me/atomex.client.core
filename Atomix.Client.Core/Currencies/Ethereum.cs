using System;
using System.Numerics;
using Atomix.Blockchain.Ethereum;
using Atomix.Core.Entities;
using Atomix.Wallet.Bip;
using Nethereum.Signer;
using Nethereum.Util;

namespace Atomix
{
    public class Ethereum : Currency
    {
        public const long WeiInEth = 1000000000000000000;
        public const long WeiInGwei = 1000000000;
        public const long GweiInEth = 1000000000;
        public const long EthDigitsMultiplier = GweiInEth; //1_000_000_000;
        public const string DefaultName = "ETH";
        public const string DefaultDescription = "Ethereum";
        public const string DefaultGasPriceFormat = "F9";
        public const string DefaultGasPriceCode = "GWEI";
        public const string DefaultFeeCode = "GAS";
        public const long DefaultGasLimit = 21000; // gas
        public const long DefaultPaymentTxGasLimit = 172000; // gas
        public const long DefaultRefundTxGasLimit = 50000; // gas
        public const long DefaultRedeemTxGasLimit = 100000; // gas
        public const decimal DefaultGasPriceInGwei = 6; // Gwei
        public const string RopstenSwapContractAddress = "0x76E5e6307A82DA2B9bDa52fd0B73BAfE17A05636";
        public const string RopstenChain = "Ropsten";
        public const string MainNetChain = "MainNet";

        public Chain Chain { get; protected set; }
        public string SwapContractAddress { get; protected set; }

        public Ethereum()
        {
            Name = DefaultName;
            Description = DefaultDescription;
            DigitsMultiplier = EthDigitsMultiplier;
            Digits = (int) Math.Log10(EthDigitsMultiplier);
            Format = $"F{Digits}";

            //FeeRate = 0;
            FeeDigits = 0; // in gas
            FeeCode = DefaultFeeCode;
            FeeFormat = $"F{FeeDigits}";

            HasFeePrice = true;
            FeePriceCode = DefaultGasPriceCode;
            FeePriceFormat = DefaultGasPriceFormat;

            Chain = Chain.Ropsten;
            SwapContractAddress = RopstenSwapContractAddress;
            BlockchainApi = new CompositeEthereumBlockchainApi(Chain);
            TransactionType = typeof(EthereumTransaction);

            IsTransactionsAvailable = true;
            IsSwapAvailable = true;
            Bip44Code = Bip44.Ethereum;
        }

        public override string AddressFromKey(byte[] publicKey)
        {
            return new EthECKey(publicKey, false)
                .GetPublicAddress()
                .ToLowerInvariant();
        }

        public override bool IsValidAddress(string address)
        {
            return new AddressUtil()
                .IsValidEthereumAddressHexFormat(address);
        }

        public override bool IsAddressFromKey(string address, byte[] publicKey)
        {
            return AddressFromKey(publicKey)
                .Equals(address.ToLowerInvariant());
        }

        public override bool VerifyMessage(byte[] publicKey, byte[] data, byte[] signature)
        {
            return new EthECKey(publicKey, false)
                .Verify(data, EthECDSASignature.FromDER(signature));
        }

        public override decimal GetFeeAmount(decimal fee, decimal feePrice)
        {
            return fee * feePrice / GweiInEth;
        }

        public override decimal GetFeeFromFeeAmount(decimal feeAmount, decimal feePrice)
        {
            return feePrice != 0
                ? feeAmount / feePrice * GweiInEth
                : 0;
        }

        public override decimal GetFeePriceFromFeeAmount(decimal feeAmount, decimal fee)
        {
            return fee != 0
                ? feeAmount / fee * GweiInEth
                : 0;
        }

        public override decimal GetDefaultFeePrice()
        {
            return DefaultGasPriceInGwei;
        }

        public static long EthToWei(decimal eth)
        {
            return (long)(eth * WeiInEth);
        }

        public static long GweiToWei(decimal gwei)
        {
            return (long)(gwei * WeiInGwei);
        }

        public static decimal WeiToEth(BigInteger wei)
        {
            return (decimal)wei / WeiInEth;
        }

        public static decimal GetDefaultFeeAmount()
        {
            return DefaultGasLimit * DefaultGasPriceInGwei / GweiInEth;
        }

        public static decimal GetDefaultPaymentFeeAmount()
        {
            return DefaultPaymentTxGasLimit * DefaultGasPriceInGwei / GweiInEth;
        }

        public static decimal GetDefaultRefundFeeAmount()
        {
            return DefaultRefundTxGasLimit * DefaultGasPriceInGwei / GweiInEth;
        }

        public static decimal GetDefaultRedeemFeeAmount()
        {
            return DefaultRedeemTxGasLimit * DefaultGasPriceInGwei / GweiInEth;
        }
    }
}