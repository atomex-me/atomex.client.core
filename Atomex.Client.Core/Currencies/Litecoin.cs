using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using NBitcoin;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BlockCypher;
using Atomex.Blockchain.Insight;
using Atomex.Blockchain.SoChain;
using Atomex.Wallet.Bip;

namespace Atomex
{
    public class Litecoin : BitcoinBasedCurrency
    {
        private const long LtcDigitsMultiplier = 100_000_000;

        public long DustThreshold { get; set; }

        public Litecoin()
        {
        }

        public Litecoin(IConfiguration configuration)
        {
            Update(configuration);
        }

        public void Update(IConfiguration configuration)
        {
            Name                    = configuration["Name"];
            Description             = configuration["Description"];
            IsToken                 = bool.Parse(configuration["IsToken"]);

            DigitsMultiplier        = LtcDigitsMultiplier;
            Digits                  = (int)Math.Log10(LtcDigitsMultiplier);
            Format                  = $"F{Digits}";

            FeeRate                 = decimal.Parse(configuration["FeeRate"]);
            DustFeeRate             = decimal.Parse(configuration["DustFeeRate"]);
            DustThreshold           = long.Parse(configuration["DustThreshold"]);

            MinTxFeeRate            = decimal.Parse(configuration["MinTxFeeRate"]);
            MinRelayTxFeeRate       = decimal.Parse(configuration["MinRelayTxFeeRate"]);

            FeeDigits               = Digits;
            FeeCode                 = Name;
            FeeFormat               = $"F{FeeDigits}";
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
            TxExplorerUri           = configuration["TxExplorerUri"];
            AddressExplorerUri      = configuration["AddressExplorerUri"];

            IsTransactionsAvailable = true;
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
                "sochain"     => (IBlockchainApi) new SoChainApi(this, configuration),
                "blockcypher" => (IBlockchainApi) new BlockCypherApi(this, configuration),
                "insight"     => (IBlockchainApi) new InsightApi(this, configuration),
                _ => throw new NotSupportedException($"BlockchainApi {blockchainApi} not supported")
            };
        }
    }
}