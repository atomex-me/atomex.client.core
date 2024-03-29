﻿using System;
using System.Globalization;

using Microsoft.Extensions.Configuration;
using NBitcoin;

using Atomex.Blockchain.Bitcoin;
using Atomex.Blockchain.Bitcoin.SoChain;
using Atomex.Blockchain.BlockCypher;
using Atomex.Common;
using Atomex.Wallets.Bips;

namespace Atomex
{
    public class LitecoinConfig : BitcoinBasedConfig
    {
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
            Name = configuration[nameof(Name)];
            DisplayedName = configuration[nameof(DisplayedName)];
            Description = configuration[nameof(Description)];
            IsToken = bool.Parse(configuration[nameof(IsToken)]);

            Decimals = int.Parse(configuration[nameof(Decimals)]);
            Format = DecimalExtensions.GetFormatWithPrecision(Decimals);

            FeeRate = decimal.Parse(configuration[nameof(FeeRate)]);
            DustFeeRate = decimal.Parse(configuration[nameof(DustFeeRate)]);
            DustThreshold = long.Parse(configuration[nameof(DustThreshold)]);

            MinTxFeeRate = decimal.Parse(configuration[nameof(MinTxFeeRate)]);
            MinRelayTxFeeRate = decimal.Parse(configuration[nameof(MinRelayTxFeeRate)]);

            FeeCode = Name;
            FeeFormat = DecimalExtensions.GetFormatWithPrecision(Decimals);
            FeeCurrencyName = Name;

            MaxRewardPercent = configuration[nameof(MaxRewardPercent)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercent)], CultureInfo.InvariantCulture)
                : 0m;
            MaxRewardPercentInBase = configuration[nameof(MaxRewardPercentInBase)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercentInBase)], CultureInfo.InvariantCulture)
                : 0m;
            FeeCurrencyToBaseSymbol = configuration[nameof(FeeCurrencyToBaseSymbol)];
            FeeCurrencySymbol = configuration[nameof(FeeCurrencySymbol)];

            HasFeePrice = false;

            Network = ResolveNetwork(configuration);
            BlockchainApi = configuration["BlockchainApi"];
            SoChainSettings = new SoChainSettings
            {
                BaseUrl = configuration["SoChain:BaseUrl"],
                Network = configuration["SoChain:Network"],
                Decimals = Decimals
            };
            BlockCypherSettings = new BlockCypherSettings
            {
                BaseUrl = configuration["BlockCypher:BaseUrl"],
                Network = configuration["BlockCypher:Network"],
                Coin = configuration["BlockCypher:Coin"],
                Decimals = Decimals
            };

            TxExplorerUri = configuration[nameof(TxExplorerUri)];
            AddressExplorerUri = configuration[nameof(AddressExplorerUri)];

            IsSwapAvailable = true;
            Bip44Code = Bip44.Litecoin;
        }

        public override long GetDust() => DustThreshold;

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

        public override BitcoinBlockchainApi GetBitcoinBlockchainApi()
        {
            return BlockchainApi.ToLowerInvariant() switch
            {
                "sochain" => new SoChainApi(Name, SoChainSettings),
                "blockcypher" => new BlockCypherApi(Name, BlockCypherSettings),
                _ => throw new NotSupportedException($"BlockchainApi {BlockchainApi} not supported")
            };
        }
    }
}