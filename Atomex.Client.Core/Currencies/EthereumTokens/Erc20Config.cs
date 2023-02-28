using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using System.Threading;

using Microsoft.Extensions.Configuration;

using Atomex.Blockchain;
using Atomex.Blockchain.Ethereum.Erc20;
using Atomex.Common;
using Atomex.Wallets.Bips;

namespace Atomex.EthereumTokens
{
    public class Erc20Config : EthereumConfig
    {
        public long TransferGasLimit { get; private set; }
        public long ApproveGasLimit { get; private set; }
        public string TokenContractAddress { get; private set; }
        public ulong TokenContractBlockNumber { get; private set; }

        public Erc20Config()
        {
        }

        public Erc20Config(IConfiguration configuration)
        {
            Update(configuration);
        }

        public override void Update(IConfiguration configuration)
        {
            Name = configuration[nameof(Name)];
            DisplayedName = configuration[nameof(DisplayedName)];
            Description = configuration[nameof(Description)];

            DustDigitsMultiplier = long.Parse(configuration[nameof(DustDigitsMultiplier)]);
            Decimals = int.Parse(configuration[nameof(Decimals)]);
            Format = DecimalExtensions.GetFormatWithPrecision(Decimals < 9 ? Decimals : 9);
            IsToken = bool.Parse(configuration[nameof(IsToken)]);

            var feeDigits = (int)Math.Round(BigInteger.Log10(new BigInteger(decimal.Parse(configuration["BaseCurrencyDigitsMultiplier"]))));
            FeeFormat = DecimalExtensions.GetFormatWithPrecision(feeDigits);
            FeeCode = "ETH";
            FeeCurrencyName = "ETH";

            HasFeePrice = true;
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

            TransferGasLimit = long.Parse(configuration[nameof(TransferGasLimit)], CultureInfo.InvariantCulture);
            ApproveGasLimit = long.Parse(configuration[nameof(ApproveGasLimit)], CultureInfo.InvariantCulture);
            InitiateGasLimit = long.Parse(configuration[nameof(InitiateGasLimit)], CultureInfo.InvariantCulture);
            InitiateWithRewardGasLimit = long.Parse(configuration[nameof(InitiateWithRewardGasLimit)], CultureInfo.InvariantCulture);
            AddGasLimit = long.Parse(configuration[nameof(AddGasLimit)], CultureInfo.InvariantCulture);
            RefundGasLimit = long.Parse(configuration[nameof(RefundGasLimit)], CultureInfo.InvariantCulture);
            RedeemGasLimit = long.Parse(configuration[nameof(RedeemGasLimit)], CultureInfo.InvariantCulture);
            EstimatedRedeemGasLimit = long.Parse(configuration[nameof(EstimatedRedeemGasLimit)], CultureInfo.InvariantCulture);
            EstimatedRedeemWithRewardGasLimit = long.Parse(configuration[nameof(EstimatedRedeemWithRewardGasLimit)], CultureInfo.InvariantCulture);
            GasPriceInGwei = decimal.Parse(configuration[nameof(GasPriceInGwei)], CultureInfo.InvariantCulture);

            MaxGasPriceInGwei = configuration[nameof(MaxGasPriceInGwei)] != null
                ? decimal.Parse(configuration[nameof(MaxGasPriceInGwei)], CultureInfo.InvariantCulture)
                : 650m;

            ChainId = int.Parse(configuration[nameof(ChainId)], CultureInfo.InvariantCulture);
            TokenContractAddress = configuration["TokenContract"];
            TokenContractBlockNumber = ulong.Parse(configuration[nameof(TokenContractBlockNumber)], CultureInfo.InvariantCulture);

            SwapContractAddress = configuration["SwapContract"];
            SwapContractBlockNumber = ulong.Parse(configuration[nameof(SwapContractBlockNumber)], CultureInfo.InvariantCulture);

            BlockchainApiBaseUri = configuration[nameof(BlockchainApiBaseUri)];
            BlockchainApi = configuration["BlockchainApi"];

            TxExplorerUri = configuration[nameof(TxExplorerUri)];
            AddressExplorerUri = configuration[nameof(AddressExplorerUri)];
            InfuraApi = configuration[nameof(InfuraApi)];
            InfuraWsApi = configuration[nameof(InfuraWsApi)];
            TransactionType = typeof(Erc20Transaction);
            TransactionMetadataType = typeof(TransactionMetadata);

            IsSwapAvailable = true;
            Bip44Code = Bip44.Ethereum; //TODO ?
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
            var (rewardForRedeemInEth, error) = await base
                .GetRewardForRedeemAsync(
                    maxRewardPercent: maxRewardPercent,
                    maxRewardPercentInBase: maxRewardPercentInBase,
                    feeCurrencyToBaseSymbol: feeCurrencyToBaseSymbol,
                    feeCurrencyToBasePrice: feeCurrencyToBasePrice,
                    feeCurrencySymbol: feeCurrencySymbol,
                    feeCurrencyPrice: feeCurrencyPrice,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (feeCurrencySymbol == null || feeCurrencyPrice == 0)
                return 0m;

            return AmountHelper.RoundDown(feeCurrencySymbol.IsBaseCurrency(Name)
                ? rewardForRedeemInEth / feeCurrencyPrice
                : rewardForRedeemInEth * feeCurrencyPrice, Precision);
        }
    }
}