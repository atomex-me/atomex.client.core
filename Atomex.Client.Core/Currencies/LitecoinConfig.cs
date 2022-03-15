using System;
using System.Globalization;

using Microsoft.Extensions.Configuration;
using NBitcoin;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BlockCypher;
using Atomex.Blockchain.Insight;
using Atomex.Blockchain.SoChain;
using Atomex.Common;
using Atomex.Wallet.Bip;

namespace Atomex
{
    public class LitecoinConfig : BitcoinBasedConfig
    {
        private const long LtcDigitsMultiplier = 100_000_000;

        public long DustThreshold { get; set; }

        public LitecoinConfig()
        {
        }

        public LitecoinConfig(IConfiguration configuration)
        {
            Update(configuration);
        }

        public void Update(IConfiguration configuration)
        {
            Name                    = configuration[nameof(Name)];
            Description             = configuration[nameof(Description)];
            IsToken                 = bool.Parse(configuration[nameof(IsToken)]);

            DigitsMultiplier        = LtcDigitsMultiplier;
            Digits                  = (int)Math.Round(Math.Log10(LtcDigitsMultiplier));
            Format                  = DecimalExtensions.GetFormatWithPrecision(Digits);

            FeeRate                 = decimal.Parse(configuration[nameof(FeeRate)]);
            DustFeeRate             = decimal.Parse(configuration[nameof(DustFeeRate)]);
            DustThreshold           = long.Parse(configuration[nameof(DustThreshold)]);

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
            Bip44Code               = Bip44.Litecoin;
        }

        public override long GetDust()
        {
            return DustThreshold;
        }

        private static Network ResolveNetwork(IConfiguration configuration)
        {
            var chain = configuration["Chain"]
                .ToLowerInvariant();

            return chain switch
            {
                "mainnet" => NBitcoin.Altcoins.Litecoin.Instance.Mainnet,
                "testnet" => NBitcoin.Altcoins.Litecoin.Instance.Testnet,
                _ => throw new NotSupportedException($"Chain {chain} not supported")
            };
        }

        private IBlockchainApi ResolveBlockchainApi(IConfiguration configuration)
        {
            var blockchainApi = configuration["BlockchainApi"]
                .ToLowerInvariant();

            return blockchainApi switch
            {
                "sochain"     => new SoChainApi(this, configuration),
                "blockcypher" => new BlockCypherApi(this, configuration),
                "insight"     => new InsightApi(this, configuration),
                _ => throw new NotSupportedException($"BlockchainApi {blockchainApi} not supported")
            };
        }
    }
}