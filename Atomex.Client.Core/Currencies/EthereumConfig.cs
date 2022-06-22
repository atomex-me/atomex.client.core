using System;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Nethereum.Signer;
using Nethereum.Util;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Core;
using Atomex.Wallet.Bip;
using Atomex.Wallets;
using Atomex.Wallets.Ethereum;

namespace Atomex
{
    public class EthereumConfig : CurrencyConfig
    {
        public const string Eth = "ETH";
        protected const long WeiInEth = 1000000000000000000;
        protected const long WeiInGwei = 1000000000;
        protected const long GweiInEth = 1000000000;
        protected const string DefaultGasPriceFormat = "F9";
        protected const string DefaultGasPriceCode = "GWEI";
        protected const string DefaultFeeCode = "GAS";
        protected const long EthDigitsMultiplier = GweiInEth; //1_000_000_000;

        public const int Mainnet = 1;
        //public const int Ropsten = 3;
        //public const int Rinkeby = 4;
        //public const int Goerli = 5;

        public decimal GasLimit { get; protected set; }
        public decimal InitiateGasLimit { get; protected set; }
        public decimal InitiateWithRewardGasLimit { get; protected set; }
        public decimal AddGasLimit { get; protected set; }
        public decimal RefundGasLimit { get; protected set; }
        public decimal RedeemGasLimit { get; protected set; }
        public decimal EstimatedRedeemGasLimit { get; protected set; }
        public decimal EstimatedRedeemWithRewardGasLimit { get; protected set; }
        public decimal GasPriceInGwei { get; protected set; }
        public decimal MaxGasPriceInGwei { get; protected set; }

        public decimal InitiateFeeAmount(decimal gasPrice) =>
            InitiateGasLimit * gasPrice / GweiInEth;

        public decimal InitiateWithRewardFeeAmount(decimal gasPrice) =>
            InitiateWithRewardGasLimit * gasPrice / GweiInEth;

        public decimal AddFeeAmount(decimal gasPrice) =>
            AddGasLimit * gasPrice / GweiInEth;

        public decimal RedeemFeeAmount(decimal gasPrice) =>
            RedeemGasLimit * gasPrice / GweiInEth;

        public decimal EstimatedRedeemFeeAmount(decimal gasPrice) =>
            EstimatedRedeemGasLimit * gasPrice / GweiInEth;

        public decimal EstimatedRedeemWithRewardFeeAmount(decimal gasPrice) =>
            EstimatedRedeemWithRewardGasLimit * gasPrice / GweiInEth;

        public int ChainId { get; protected set; }
        public string BlockchainApiBaseUri { get; protected set; }
        public string SwapContractAddress { get; protected set; }
        public ulong SwapContractBlockNumber { get; protected set; }
        public string InfuraApi { get; protected set; }
        public string InfuraWsApi { get; protected set; }

        public EthereumConfig()
        {
        }

        public EthereumConfig(IConfiguration configuration)
        {
            Update(configuration);
        }

        public virtual void Update(IConfiguration configuration)
        {
            Name                       = configuration[nameof(Name)];
            DisplayedName              = configuration[nameof(DisplayedName)];
            Description                = configuration[nameof(Description)];
            DigitsMultiplier           = EthDigitsMultiplier;
            Digits                     = (int)Math.Round(Math.Log10(EthDigitsMultiplier));
            Format                     = DecimalExtensions.GetFormatWithPrecision(Digits);
            IsToken                    = bool.Parse(configuration[nameof(IsToken)]);

            FeeCode                    = Name;
            FeeFormat                  = DecimalExtensions.GetFormatWithPrecision(Digits);
            FeeCurrencyName            = Name;

            HasFeePrice                = true;
            FeePriceCode               = DefaultGasPriceCode;
            FeePriceFormat             = DefaultGasPriceFormat;

            MaxRewardPercent           = configuration[nameof(MaxRewardPercent)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercent)], CultureInfo.InvariantCulture)
                : 0m;
            MaxRewardPercentInBase     = configuration[nameof(MaxRewardPercentInBase)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercentInBase)], CultureInfo.InvariantCulture)
                : 0m;
            FeeCurrencyToBaseSymbol    = configuration[nameof(FeeCurrencyToBaseSymbol)];
            FeeCurrencySymbol          = configuration[nameof(FeeCurrencySymbol)];

            GasLimit                   = decimal.Parse(configuration[nameof(GasLimit)], CultureInfo.InvariantCulture);
            InitiateGasLimit           = decimal.Parse(configuration[nameof(InitiateGasLimit)], CultureInfo.InvariantCulture);
            InitiateWithRewardGasLimit = decimal.Parse(configuration[nameof(InitiateWithRewardGasLimit)], CultureInfo.InvariantCulture);
            AddGasLimit                = decimal.Parse(configuration[nameof(AddGasLimit)], CultureInfo.InvariantCulture);
            RefundGasLimit             = decimal.Parse(configuration[nameof(RefundGasLimit)], CultureInfo.InvariantCulture);
            RedeemGasLimit             = decimal.Parse(configuration[nameof(RedeemGasLimit)], CultureInfo.InvariantCulture);
            EstimatedRedeemGasLimit    = decimal.Parse(configuration[nameof(EstimatedRedeemGasLimit)], CultureInfo.InvariantCulture);
            EstimatedRedeemWithRewardGasLimit = decimal.Parse(configuration[nameof(EstimatedRedeemWithRewardGasLimit)], CultureInfo.InvariantCulture);
            GasPriceInGwei             = decimal.Parse(configuration[nameof(GasPriceInGwei)], CultureInfo.InvariantCulture);

            MaxGasPriceInGwei = configuration[nameof(MaxGasPriceInGwei)] != null
                ? decimal.Parse(configuration[nameof(MaxGasPriceInGwei)], CultureInfo.InvariantCulture)
                : 650m;

            ChainId                    = int.Parse(configuration[nameof(ChainId)], CultureInfo.InvariantCulture);
            SwapContractAddress        = configuration["SwapContract"];
            SwapContractBlockNumber    = ulong.Parse(configuration[nameof(SwapContractBlockNumber)], CultureInfo.InvariantCulture);

            BlockchainApiBaseUri       = configuration[nameof(BlockchainApiBaseUri)];
            BlockchainApi              = ResolveBlockchainApi(
                configuration: configuration,
                currency: this);

            TxExplorerUri              = configuration[nameof(TxExplorerUri)];
            AddressExplorerUri         = configuration[nameof(AddressExplorerUri)];
            InfuraApi                  = configuration[nameof(InfuraApi)];
            InfuraWsApi                = configuration[nameof(InfuraWsApi)];
            TransactionType            = typeof(EthereumTransaction);

            IsSwapAvailable            = true;
            Bip44Code                  = Bip44.Ethereum;
        }

        protected static IBlockchainApi ResolveBlockchainApi(
            IConfiguration configuration,
            EthereumConfig currency)
        {
            var blockchainApi = configuration["BlockchainApi"]
                .ToLowerInvariant();

            return blockchainApi switch
            {
                "etherscan" => new EtherScanApi(currency.Name, currency.BlockchainApiBaseUri),
                _ => throw new NotSupportedException($"BlockchainApi {blockchainApi} not supported")
            };
        }

        public override IExtKey CreateExtKey(SecureBytes seed, int keyType) =>
            new EthereumExtKey(seed);

        public override IKey CreateKey(SecureBytes seed) =>
            new EthereumKey(seed);

        public override string AddressFromKey(byte[] publicKey) =>
            new EthECKey(publicKey, false)
                .GetPublicAddress()
                .ToLowerInvariant();

        public override bool IsValidAddress(string address) =>
            new AddressUtil()
                .IsValidEthereumAddressHexFormat(address);

        public override bool IsAddressFromKey(string address, byte[] publicKey) =>
            AddressFromKey(publicKey).ToLowerInvariant()
                .Equals(address.ToLowerInvariant());

        //public override bool VerifyMessage(byte[] data, byte[] signature, byte[] publicKey) =>
        //    new EthECKey(publicKey, false)
        //        .Verify(data, EthECDSASignature.FromDER(signature));

        public override decimal GetFeeAmount(decimal fee, decimal feePrice) =>
            fee * feePrice / GweiInEth;

        public override decimal GetFeeFromFeeAmount(decimal feeAmount, decimal feePrice) =>
            feePrice != 0
                ? Math.Floor(feeAmount / feePrice * GweiInEth)
                : 0;

        public override decimal GetFeePriceFromFeeAmount(decimal feeAmount, decimal fee) =>
            fee != 0
                ? Math.Floor(feeAmount / fee * GweiInEth)
                : 0;

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

        public override async Task<decimal> GetEstimatedRedeemFeeAsync(
            WalletAddress toAddress = null,
            bool withRewardForRedeem = false,
            CancellationToken cancellationToken = default)
        {
            var gasPrice = await GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            return withRewardForRedeem
                ? EstimatedRedeemWithRewardFeeAmount(gasPrice)
                : EstimatedRedeemFeeAmount(gasPrice);
        }

        public override async Task<decimal> GetRewardForRedeemAsync(
            decimal maxRewardPercent,
            decimal maxRewardPercentInBase,
            string feeCurrencyToBaseSymbol,
            decimal feeCurrencyToBasePrice,
            string feeCurrencySymbol = null,
            decimal feeCurrencyPrice = 0,
            CancellationToken cancellationToken = default)
        {
            if (maxRewardPercent == 0 || maxRewardPercentInBase == 0)
                return 0m;

            var gasPrice = await GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var redeemFeeInEth = EstimatedRedeemWithRewardFeeAmount(gasPrice);

            return CalculateRewardForRedeem(
                redeemFee: redeemFeeInEth,
                redeemFeeCurrency: "ETH",
                redeemFeeDigitsMultiplier: EthDigitsMultiplier,
                maxRewardPercent: maxRewardPercent,
                maxRewardPercentValue: maxRewardPercentInBase,
                feeCurrencyToBaseSymbol: feeCurrencyToBaseSymbol,
                feeCurrencyToBasePrice: feeCurrencyToBasePrice);
        }

        public override Task<decimal> GetDefaultFeePriceAsync(
            CancellationToken cancellationToken = default) =>
            GetGasPriceAsync(cancellationToken);

        public override decimal GetDefaultFee() =>
            GasLimit;

        public async Task<decimal> GetGasPriceAsync(
            CancellationToken cancellationToken = default)
        {
            if (ChainId != Mainnet)
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
                    ? Math.Min(gasPrice.Value.High * 1.2m, MaxGasPriceInGwei)
                    : GasPriceInGwei;
            }
            catch (Exception e)
            {
                Log.Error(e, "Get gas price error");

                return GasPriceInGwei;
            }
        }

        public static BigInteger EthToWei(decimal eth) =>
            new(eth * WeiInEth);

        public static long GweiToWei(decimal gwei) =>
            (long)(gwei * WeiInGwei);
        
        public static long WeiToGwei(decimal wei) =>
            (long)(wei / WeiInGwei);

        public static decimal WeiToEth(BigInteger wei) =>
            (decimal)wei / WeiInEth;
    }
}