using System;
using Microsoft.Extensions.Configuration;
using NBitcoin;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Insight;
using Atomex.Wallet.Bips;

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
            Name = configuration["Name"];
            Description = configuration["Description"];

            DigitsMultiplier = LtcDigitsMultiplier;
            Digits = (int)Math.Log10(LtcDigitsMultiplier);
            Format = $"F{Digits}";

            FeeRate = decimal.Parse(configuration["FeeRate"]);
            DustFeeRate = decimal.Parse(configuration["DustFeeRate"]);
            DustThreshold = long.Parse(configuration["DustThreshold"]);

            MinTxFeeRate = decimal.Parse(configuration["MinTxFeeRate"]);
            MinRelayTxFeeRate = decimal.Parse(configuration["MinRelayTxFeeRate"]);

            FeeDigits = Digits;
            FeeCode = Name;
            FeeFormat = $"F{FeeDigits}";
            FeeCurrencyName = Name;

            HasFeePrice = false;

            Network = ResolveNetwork(configuration);
            BlockchainApi = ResolveBlockchainApi(configuration);
            TxExplorerUri = configuration["TxExplorerUri"];
            AddressExplorerUri = configuration["AddressExplorerUri"];

            IsTransactionsAvailable = true;
            IsSwapAvailable = true;
            Bip44Code = Bip44.Litecoin;
        }

        public override long GetDust()
        {
            return DustThreshold;
        }

        private static Network ResolveNetwork(IConfiguration configuration)
        {
            var chain = configuration["Chain"]
                .ToLowerInvariant();

            if (chain.Equals("mainnet"))
                return NBitcoin.Altcoins.Litecoin.Instance.Mainnet; 

            if (chain.Equals("testnet"))
                return NBitcoin.Altcoins.Litecoin.Instance.Testnet;

            throw new NotSupportedException($"Chain {chain} not supported");
        }

        private IBlockchainApi ResolveBlockchainApi(IConfiguration configuration)
        {
            var blockchainApi = configuration["BlockchainApi"]
                .ToLowerInvariant();

            if (blockchainApi.Equals("insight"))
                return new InsightApi(this, configuration);

            throw new NotSupportedException($"BlockchainApi {blockchainApi} not supported");
        }
    }
}