using System;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Nethereum.Signer;
using Nethereum.Util;

using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Blockchain.Ethereum.EtherScan;
using Atomex.Blockchain.Ethereum.Erc20.Abstract;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Core;
using Atomex.Wallets;
using Atomex.Wallets.Bips;
using Atomex.Wallets.Ethereum;

namespace Atomex
{
    public class EthereumConfig : CurrencyConfig
    {
        protected const string DefaultGasPriceFormat = "F9";
        protected const string DefaultGasPriceCode = "GWEI";
        protected const string DefaultFeeCode = "GAS";
        protected const long EthDigitsMultiplier = EthereumHelper.GweiInEth; //1_000_000_000;
        protected const decimal MaxFeePerGasCoeff = 1.2m;
        protected const long MinPriorityFeePerGas = 1;
        protected const int EthDecimals = 18;
        protected const int DisplayedDecimals = 9;

        public const int Mainnet = 1;
        //public const int Ropsten = 3;
        //public const int Rinkeby = 4;
        //public const int Goerli = 5;

        public long GasLimit { get; protected set; }
        public long InitiateGasLimit { get; protected set; }
        public long InitiateWithRewardGasLimit { get; protected set; }
        public long AddGasLimit { get; protected set; }
        public long RefundGasLimit { get; protected set; }
        public long RedeemGasLimit { get; protected set; }
        public long EstimatedRedeemGasLimit { get; protected set; }
        public long EstimatedRedeemWithRewardGasLimit { get; protected set; }

        public int ChainId { get; protected set; }
        public string BlockchainApiBaseUri { get; protected set; }
        public string SwapContractAddress { get; protected set; }
        public ulong SwapContractBlockNumber { get; protected set; }
        public string InfuraApi { get; protected set; }
        public string InfuraWsApi { get; protected set; }
        public EtherScanSettings EtherScanSettings { get; protected set; }

        public EthereumConfig()
        {
        }

        public EthereumConfig(IConfiguration configuration)
        {
            Update(configuration);
        }

        public virtual void Update(IConfiguration configuration)
        {
            Name = configuration[nameof(Name)];
            DisplayedName = configuration[nameof(DisplayedName)];
            Description = configuration[nameof(Description)];

            Decimals = int.Parse(configuration[nameof(Decimals)]);
            Format = DecimalExtensions.GetFormatWithPrecision(DisplayedDecimals);
            IsToken = bool.Parse(configuration[nameof(IsToken)]);

            FeeCode = Name;
            FeeFormat = DecimalExtensions.GetFormatWithPrecision(DisplayedDecimals);
            FeeCurrencyName = Name;

            HasFeePrice  = true;
            FeePriceCode = DefaultGasPriceCode;
            FeePriceFormat = DefaultGasPriceFormat;

            MaxRewardPercent = configuration[nameof(MaxRewardPercent)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercent)], CultureInfo.InvariantCulture)
                : 0m;
            MaxRewardPercentInBase = configuration[nameof(MaxRewardPercentInBase)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercentInBase)], CultureInfo.InvariantCulture)
                : 0m;
            FeeCurrencyToBaseSymbol = configuration[nameof(FeeCurrencyToBaseSymbol)];
            FeeCurrencySymbol = configuration[nameof(FeeCurrencySymbol)];

            GasLimit = long.Parse(configuration[nameof(GasLimit)], CultureInfo.InvariantCulture);
            InitiateGasLimit = long.Parse(configuration[nameof(InitiateGasLimit)], CultureInfo.InvariantCulture);
            InitiateWithRewardGasLimit = long.Parse(configuration[nameof(InitiateWithRewardGasLimit)], CultureInfo.InvariantCulture);
            AddGasLimit = long.Parse(configuration[nameof(AddGasLimit)], CultureInfo.InvariantCulture);
            RefundGasLimit = long.Parse(configuration[nameof(RefundGasLimit)], CultureInfo.InvariantCulture);
            RedeemGasLimit = long.Parse(configuration[nameof(RedeemGasLimit)], CultureInfo.InvariantCulture);
            EstimatedRedeemGasLimit = long.Parse(configuration[nameof(EstimatedRedeemGasLimit)], CultureInfo.InvariantCulture);
            EstimatedRedeemWithRewardGasLimit = long.Parse(configuration[nameof(EstimatedRedeemWithRewardGasLimit)], CultureInfo.InvariantCulture);

            ChainId = int.Parse(configuration[nameof(ChainId)], CultureInfo.InvariantCulture);
            SwapContractAddress = configuration["SwapContract"];
            SwapContractBlockNumber = ulong.Parse(configuration[nameof(SwapContractBlockNumber)], CultureInfo.InvariantCulture);

            BlockchainApiBaseUri = configuration[nameof(BlockchainApiBaseUri)];
            BlockchainApi = configuration["BlockchainApi"];

            EtherScanSettings = new EtherScanSettings
            {
                BaseUri = BlockchainApiBaseUri
            };

            TxExplorerUri = configuration[nameof(TxExplorerUri)];
            AddressExplorerUri = configuration[nameof(AddressExplorerUri)];
            InfuraApi = configuration[nameof(InfuraApi)];
            InfuraWsApi = configuration[nameof(InfuraWsApi)];
            TransactionType = typeof(EthereumTransaction);
            TransactionMetadataType = typeof(TransactionMetadata);

            IsSwapAvailable = true;
            Bip44Code = Bip44.Ethereum;
        }

        public override IBlockchainApi GetBlockchainApi() => GetEtherScanApi();
        public IEthereumApi GetEthereumApi() => GetEtherScanApi();
        public IErc20Api GetErc20Api() => GetEtherScanApi();
        public IGasPriceProvider GetGasPriceProvider() => GetEtherScanApi();
        public EtherScanApi GetEtherScanApi() => new(EtherScanSettings);

        public override IExtKey CreateExtKey(SecureBytes seed, int keyType) =>
            new EthereumExtKey(seed);

        public override IKey CreateKey(SecureBytes seed) =>
            new EthereumKey(seed);

        public override string AddressFromKey(byte[] publicKey, int keyType) =>
            new EthECKey(publicKey, false)
                .GetPublicAddress()
                .ToLowerInvariant();

        public override bool IsValidAddress(string address) =>
            new AddressUtil()
                .IsValidEthereumAddressHexFormat(address);

        public decimal GetFeeInEth(long gasLimit, decimal gasPrice) =>
            gasLimit * gasPrice / EthereumHelper.GweiInEth;

        public decimal GetGasPriceInGwei(BigInteger valueInWei, long gasLimit) =>
            (decimal)(valueInWei / gasLimit / EthereumHelper.WeiInGwei);

        public override async Task<Result<BigInteger>> GetPaymentFeeAsync(
            CancellationToken cancellationToken = default)
        {
            var (gasPrice, error) = await GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return InitiateGasLimit * gasPrice.MaxFeePerGas.GweiToWei();
        }

        public override async Task<Result<BigInteger>> GetRedeemFeeAsync(
            WalletAddress toAddress = null,
            CancellationToken cancellationToken = default)
        {
            var (gasPrice, error) = await GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return RedeemGasLimit * gasPrice.MaxFeePerGas.GweiToWei();
        }

        public override async Task<Result<BigInteger>> GetEstimatedRedeemFeeAsync(
            WalletAddress toAddress = null,
            bool withRewardForRedeem = false,
            CancellationToken cancellationToken = default)
        {
            var (gasPrice, error) = await GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (error != null) 
                return error;

            return withRewardForRedeem
                ? EstimatedRedeemWithRewardGasLimit * gasPrice.MaxFeePerGas.GweiToWei()
                : EstimatedRedeemGasLimit * gasPrice.MaxFeePerGas.GweiToWei();
        }

        public override async Task<Result<decimal>> GetRewardForRedeemAsync(
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

            var (gasPrice, error) = await GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            var redeemFeeInEth = GetFeeInEth(EstimatedRedeemWithRewardGasLimit, gasPrice.MaxFeePerGas);

            return CalculateRewardForRedeem(
                redeemFee: redeemFeeInEth,
                redeemFeeCurrency: "ETH",
                redeemFeePrecision: Precision,
                maxRewardPercent: maxRewardPercent,
                maxRewardPercentValue: maxRewardPercentInBase,
                feeCurrencyToBaseSymbol: feeCurrencyToBaseSymbol,
                feeCurrencyToBasePrice: feeCurrencyToBasePrice);
        }

        private static DateTime _lastGasPriceTimeStamp;
        private static GasPrice _lastGasPrice;
        private const int GAS_PRICE_CACHE_IN_SEC = 10;

        public async Task<Result<GasPrice>> GetGasPriceAsync(
            CancellationToken cancellationToken = default)
        {
            if (ChainId != Mainnet)
            {
                return new GasPrice
                {
                    LastBlock            = 0,
                    LowGasPrice          = 10,
                    AverageGasPrice      = 11,
                    HighGasPrice         = 12,
                    SuggestBaseFee       = 10,
                    MaxFeePerGas         = 12 * MaxFeePerGasCoeff,
                    MaxPriorityFeePerGas = MinPriorityFeePerGas,
                };
            }

            if (_lastGasPrice != null && DateTime.UtcNow - _lastGasPriceTimeStamp < TimeSpan.FromSeconds(GAS_PRICE_CACHE_IN_SEC))
                return _lastGasPrice;

            var gasPriceProvider = GetGasPriceProvider();

            var (gasPrice, error) = await gasPriceProvider
                .GetOracleGasPriceAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            var highGasPrice = long.Parse(gasPrice.FastGasPrice);
            var suggestBaseFee = decimal.Parse(gasPrice.SuggestBaseFee, CultureInfo.InvariantCulture);

            var result = new GasPrice
            {
                LastBlock            = long.Parse(gasPrice.LastBlock),
                LowGasPrice          = long.Parse(gasPrice.SafeGasPrice),
                AverageGasPrice      = long.Parse(gasPrice.ProposeGasPrice),
                HighGasPrice         = highGasPrice,
                SuggestBaseFee       = suggestBaseFee,
                MaxFeePerGas         = highGasPrice * MaxFeePerGasCoeff,
                MaxPriorityFeePerGas = Math.Max(highGasPrice - Math.Floor(suggestBaseFee), MinPriorityFeePerGas),
            };

            _lastGasPrice = result;
            _lastGasPriceTimeStamp = DateTime.UtcNow;

            return result;
        }
    }
}