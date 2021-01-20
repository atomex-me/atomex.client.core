using System;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Nethereum.Signer;
using Nethereum.Util;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography;
using Atomex.Wallet.Bip;
using Atomex.Wallet.Ethereum;
using Atomex.Blockchain.Ethereum.Abstract;
using Serilog;

namespace Atomex
{
    public class Ethereum : Currency
    {
        protected const long WeiInEth = 1000000000000000000;
        protected const long WeiInGwei = 1000000000;
        protected const long GweiInEth = 1000000000;
        protected const string DefaultGasPriceFormat = "F9";
        protected const string DefaultGasPriceCode = "GWEI";
        protected const string DefaultFeeCode = "GAS";
        protected const long EthDigitsMultiplier = GweiInEth; //1_000_000_000;

        public decimal GasLimit { get; protected set; }
        public decimal InitiateGasLimit { get; protected set; }
        public decimal InitiateWithRewardGasLimit { get; protected set; }
        public decimal AddGasLimit { get; protected set; }
        public decimal RefundGasLimit { get; protected set; }
        public decimal RedeemGasLimit { get; protected set; }
        public decimal GasPriceInGwei { get; protected set; }

        public decimal InitiateFeeAmount(decimal gasPrice) =>
            InitiateGasLimit * gasPrice / GweiInEth;

        public decimal InitiateWithRewardFeeAmount(decimal gasPrice) =>
            InitiateWithRewardGasLimit * gasPrice / GweiInEth;

        public decimal AddFeeAmount(decimal gasPrice) =>
            AddGasLimit * gasPrice / GweiInEth;

        public decimal RedeemFeeAmount(decimal gasPrice) =>
            RedeemGasLimit * gasPrice / GweiInEth;

        public Chain Chain { get; protected set; }
        public string BlockchainApiBaseUri { get; protected set; }
        public string SwapContractAddress { get; protected set; }
        public ulong SwapContractBlockNumber { get; protected set; }

        public Ethereum()
        {
        }

        public Ethereum(IConfiguration configuration)
        {
            Update(configuration);
        }

        public virtual void Update(IConfiguration configuration)
        {
            Name = configuration["Name"];
            Description = configuration["Description"];
            DigitsMultiplier = EthDigitsMultiplier;
            Digits = (int)Math.Log10(EthDigitsMultiplier);
            Format = $"F{Digits}";

            FeeDigits = Digits; // in gas
            FeeCode = Name;
            FeeFormat = $"F{FeeDigits}";
            FeeCurrencyName = Name;

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
                currency: this);

            TxExplorerUri = configuration["TxExplorerUri"];
            AddressExplorerUri = configuration["AddressExplorerUri"];
            TransactionType = typeof(EthereumTransaction);

            IsTransactionsAvailable = true;
            IsSwapAvailable = true;
            Bip44Code = Bip44.Ethereum;
        }

        protected static Chain ResolveChain(IConfiguration configuration)
        {
            var chain = configuration["Chain"]
                .ToLowerInvariant();

            if (chain.Equals("mainnet"))
                return Chain.MainNet;

            if (chain.Equals("ropsten"))
                return Chain.Ropsten;

            throw new NotSupportedException($"Chain {chain} not supported");
        }

        protected static IBlockchainApi ResolveBlockchainApi(
            IConfiguration configuration,
            Ethereum currency)
        {
            var blockchainApi = configuration["BlockchainApi"]
                .ToLowerInvariant();

            if (blockchainApi.Equals("etherscan"))
                return new EtherScanApi(currency);

            throw new NotSupportedException($"BlockchainApi {blockchainApi} not supported");
        }

        public override IExtKey CreateExtKey(SecureBytes seed)
        {
            return new EthereumExtKey(seed);
        }

        public override IKey CreateKey(SecureBytes seed)
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
                ? Math.Floor(feeAmount / feePrice * GweiInEth)
                : 0;
        }

        public override decimal GetFeePriceFromFeeAmount(decimal feeAmount, decimal fee)
        {
            return fee != 0
                ? Math.Floor(feeAmount / fee * GweiInEth)
                : 0;
        }

        public override async Task<decimal> GetPaymentFeeAsync(
            CancellationToken cancellationToken = default)
        {
            var gasPrice = await GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            return InitiateFeeAmount(gasPrice);
        }

        public override async Task<decimal> GetRedeemFeeAsync(
            WalletAddress toAddress = null,
            CancellationToken cancellationToken = default)
        {
            var gasPrice = await GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            return RedeemFeeAmount(gasPrice);
        }

        public override async Task<decimal> GetRewardForRedeemAsync(
            CancellationToken cancellationToken = default)
        {
            var gasPrice = await GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            return RedeemFeeAmount(gasPrice);
        }

        public override Task<decimal> GetDefaultFeePriceAsync(
            CancellationToken cancellationToken = default)
        {
            return GetGasPriceAsync(cancellationToken);
        }

        public override decimal GetDefaultFee() => GasLimit;

        public async Task<decimal> GetGasPriceAsync(
            CancellationToken cancellationToken = default)
        {
            if (Chain != Chain.MainNet)
                return GasPriceInGwei;

            var gasPriceProvider = BlockchainApi as IGasPriceProvider;

            try
            {
                var gasPrice = await gasPriceProvider
                    .GetGasPriceAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (gasPrice == null || gasPrice.HasError || gasPrice.Value == null)
                    Log.Error("Invalid gas price!" + ((gasPrice?.HasError ?? false) ? " " + gasPrice.Error.Description : ""));

                return gasPrice != null && !gasPrice.HasError && gasPrice.Value != null
                    ? Math.Min(gasPrice.Value.Average * 1.3m, gasPrice.Value.High)
                    : GasPriceInGwei;
            }
            catch (Exception e)
            {
                Log.Error(e, "Get gas price error");

                return GasPriceInGwei;
            }
        }

        public static BigInteger EthToWei(decimal eth) => new BigInteger(eth * WeiInEth);

        public static long GweiToWei(decimal gwei) => (long)(gwei * WeiInGwei);
        
        public static long WeiToGwei(decimal wei) => (long)(wei / WeiInGwei);

        public static decimal WeiToEth(BigInteger wei) => (decimal)wei / WeiInEth;
    }
}