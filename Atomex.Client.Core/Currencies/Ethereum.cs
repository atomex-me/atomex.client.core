using System;
using System.Globalization;
using System.Numerics;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Core.Entities;
using Atomex.Cryptography;
using Atomex.Wallet.Bip;
using Atomex.Wallet.Ethereum;
using Microsoft.Extensions.Configuration;
using Nethereum.Signer;
using Nethereum.Util;

namespace Atomex
{
    public class Ethereum : Currency
    {
        private const long WeiInEth = 1000000000000000000;
        private const long WeiInGwei = 1000000000;
        private const long GweiInEth = 1000000000;
        private const string DefaultGasPriceFormat = "F9";
        private const string DefaultGasPriceCode = "GWEI";
        private const string DefaultFeeCode = "GAS";
        private const long EthDigitsMultiplier = GweiInEth; //1_000_000_000;

        public decimal GasLimit { get; private set; }
        public decimal InitiateGasLimit { get; private set; }
        public decimal InitiateWithRewardGasLimit { get; private set; }
        public decimal AddGasLimit { get; private set; }
        public decimal RefundGasLimit { get; private set; }
        public decimal RedeemGasLimit { get; private set; }
        public decimal GasPriceInGwei { get; private set; }
        public decimal InitiateFeeAmount => InitiateGasLimit * GasPriceInGwei / GweiInEth;
        public decimal InitiateWithRewardFeeAmount => InitiateWithRewardGasLimit * GasPriceInGwei / GweiInEth;
        public decimal AddFeeAmount => AddGasLimit * GasPriceInGwei / GweiInEth;
        public decimal RefundFeeAmount => RefundGasLimit * GasPriceInGwei / GweiInEth;
        public decimal RedeemFeeAmount => RedeemGasLimit * GasPriceInGwei / GweiInEth;

        public Chain Chain { get; private set; }
        public string BlockchainApiBaseUri { get; private set; }
        public string SwapContractAddress { get; private set; }
        public ulong SwapContractBlockNumber { get; private set; }

        public Ethereum()
        {
        }

        public Ethereum(IConfiguration configuration)
        {
            Update(configuration);
        }

        public void Update(IConfiguration configuration)
        {
            Name = configuration["Name"];
            Description = configuration["Description"];
            DigitsMultiplier = EthDigitsMultiplier;
            Digits = (int)Math.Log10(EthDigitsMultiplier);
            Format = $"F{Digits}";

            FeeDigits = 0; // in gas
            FeeCode = DefaultFeeCode;
            FeeFormat = $"F{FeeDigits}";

            HasFeePrice = true;
            FeePriceCode = DefaultGasPriceCode;
            FeePriceFormat = DefaultGasPriceFormat;

            GasLimit = decimal.Parse(configuration["GasLimit"], CultureInfo.InvariantCulture);
            InitiateGasLimit = decimal.Parse(configuration["InitiateGasLimit"], CultureInfo.InvariantCulture);
            InitiateWithRewardGasLimit = decimal.Parse(configuration["InitiateWithRewardGasLimit"], CultureInfo.InvariantCulture);
            AddGasLimit = decimal.Parse(configuration["AddGasLimit"], CultureInfo.InvariantCulture);
            RefundGasLimit = decimal.Parse(configuration["RefundGasLimit"], CultureInfo.InvariantCulture);
            RedeemGasLimit = decimal.Parse(configuration["RedeemGasLimit"], CultureInfo.InvariantCulture);
            GasPriceInGwei = decimal.Parse(configuration["GasPriceInGwei"], CultureInfo.InvariantCulture);

            Chain = ResolveChain(configuration);
            SwapContractAddress = configuration["SwapContract"];
            SwapContractBlockNumber = ulong.Parse(configuration["SwapContractBlockNumber"], CultureInfo.InvariantCulture);

            BlockchainApiBaseUri = configuration["BlockchainApiBaseUri"];
            BlockchainApi = ResolveBlockchainApi(
                configuration: configuration,
                currency: this,
                chain: Chain);

            TxExplorerUri = configuration["TxExplorerUri"];
            AddressExplorerUri = configuration["AddressExplorerUri"];
            TransactionType = typeof(EthereumTransaction);

            IsTransactionsAvailable = true;
            IsSwapAvailable = true;
            Bip44Code = Bip44.Ethereum;
        }

        private static Chain ResolveChain(IConfiguration configuration)
        {
            var chain = configuration["Chain"]
                .ToLowerInvariant();

            if (chain.Equals("mainnet"))
                return Chain.MainNet;

            if (chain.Equals("ropsten"))
                return Chain.Ropsten;

            throw new NotSupportedException($"Chain {chain} not supported");
        }

        private static IBlockchainApi ResolveBlockchainApi(
            IConfiguration configuration,
            Ethereum currency,
            Chain chain)
        {
            var blockchainApi = configuration["BlockchainApi"]
                .ToLowerInvariant();

            if (blockchainApi.Equals("etherscan+web3"))
                return new CompositeEthereumBlockchainApi(currency, chain);

            throw new NotSupportedException($"BlockchainApi {blockchainApi} not supported");
        }

        public override IExtKey CreateExtKey(byte[] seed)
        {
            return new EthereumExtKey(seed);
        }

        public override IKey CreateKey(byte[] seed)
        {
            return new EthereumKey(seed);
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
            return AddressFromKey(publicKey).ToLowerInvariant()
                .Equals(address.ToLowerInvariant());
        }

        public override bool VerifyMessage(byte[] data, byte[] signature, byte[] publicKey)
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

        public override decimal GetDefaultRedeemFee()
        {
            return RedeemFeeAmount;
        }

        public override decimal GetDefaultFeePrice()
        {
            return GasPriceInGwei;
        }

        public static BigInteger EthToWei(decimal eth)
        {
            return new BigInteger(eth * WeiInEth);
        }

        public static long GweiToWei(decimal gwei)
        {
            return (long)(gwei * WeiInGwei);
        }

        public static decimal WeiToEth(BigInteger wei)
        {
            return (decimal)wei / WeiInEth;
        }
    }
}