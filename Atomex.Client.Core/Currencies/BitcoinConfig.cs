using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using NBitcoin;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Blockchain.BitCore;
using Atomex.Blockchain.BlockCypher;
using Atomex.Blockchain.Insight;
using Atomex.Blockchain.SoChain;
using Atomex.Common;
using Atomex.Wallet.Bip;
using FeeRate = Atomex.Blockchain.BitcoinBased.FeeRate;

namespace Atomex
{
    public class BitcoinConfig : BitcoinBasedConfig
    {
        private const long BtcDigitsMultiplier = 100_000_000;

        public BitcoinConfig()
        {
        }

        public BitcoinConfig(IConfiguration configuration)
        {
            Update(configuration);
        }

        public void Update(IConfiguration configuration)
        {
            Name                    = configuration[nameof(Name)];
            Description             = configuration[nameof(Description)];
            IsToken                 = bool.Parse(configuration[nameof(IsToken)]);
 
            DigitsMultiplier        = BtcDigitsMultiplier;
            Digits                  = (int)Math.Round(Math.Log10(BtcDigitsMultiplier));
            Format                  = DecimalExtensions.GetFormatWithPrecision(Digits);

            FeeRate                 = decimal.Parse(configuration[nameof(FeeRate)]);
            DustFeeRate             = decimal.Parse(configuration[nameof(DustFeeRate)]);
            MinTxFeeRate            = decimal.Parse(configuration[nameof(MinTxFeeRate)]);
            MinRelayTxFeeRate       = decimal.Parse(configuration[nameof(MinRelayTxFeeRate)]);

            FeeCode                 = Name;
            FeeFormat               = DecimalExtensions.GetFormatWithPrecision(Digits);
            FeeCurrencyName         = Name;

            MaxRewardPercent        = configuration[nameof(MaxRewardPercent)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercent)], CultureInfo.InvariantCulture)
                : 0m;
            MaxRewardPercentInBase  = configuration[nameof(MaxRewardPercentInBase)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercentInBase)], CultureInfo.InvariantCulture)
                : 0m;
            FeeCurrencyToBaseSymbol = configuration[nameof(FeeCurrencyToBaseSymbol)];
            FeeCurrencySymbol       = configuration[nameof(FeeCurrencySymbol)];

            HasFeePrice             = false;

            Network                 = ResolveNetwork(configuration);
            BlockchainApi           = ResolveBlockchainApi(configuration);
            TxExplorerUri           = configuration[nameof(TxExplorerUri)];
            AddressExplorerUri      = configuration[nameof(AddressExplorerUri)];

            IsSwapAvailable         = true;
            Bip44Code               = Bip44.Bitcoin;
        }

        private static Network ResolveNetwork(IConfiguration configuration)
        {
            var chain = configuration["Chain"]
                .ToLowerInvariant();

            return chain switch
            {
                "mainnet" => Network.Main,
                "testnet" => Network.TestNet,
                _ => throw new NotSupportedException($"Chain {chain} not supported")
            };
        }

        private IBlockchainApi ResolveBlockchainApi(IConfiguration configuration)
        {
            var blockchainApi = configuration["BlockchainApi"]
                .ToLowerInvariant();

            return blockchainApi switch
            {
                "sochain"             => new SoChainApi(this, configuration),
                "blockcypher"         => new BlockCypherApi(this, configuration),
                "insight"             => new InsightApi(this, configuration),
                "bitcore+blockcypher" => new BitCoreApi(this, configuration),
                _ => throw new NotSupportedException($"BlockchainApi {blockchainApi} not supported")
            };
        }

        private FeeRate _feeRate;
        private DateTime _feeRateTimeStampUtc;

        public override async Task<decimal> GetFeeRateAsync(
            bool useCache = true,
            CancellationToken cancellationToken = default)
        {
            if (Network != Network.Main)
                return FeeRate;

            if (useCache &&
                _feeRate != null &&
                DateTime.UtcNow - _feeRateTimeStampUtc < TimeSpan.FromMinutes(3))
            {
                return _feeRate.FastestFee;
            }

            try
            {
                var feeRateResult = await BitcoinFeesEarn
                    .GetFeeRateAsync(cancellationToken)
                    .ConfigureAwait(false);

                _feeRate = feeRateResult?.Value;
                _feeRateTimeStampUtc = DateTime.UtcNow;

                return feeRateResult != null && !feeRateResult.HasError && feeRateResult.Value != null
                    ? feeRateResult.Value.FastestFee
                    : FeeRate;
            }
            catch
            {
                return FeeRate;
            }
        }
    }
}