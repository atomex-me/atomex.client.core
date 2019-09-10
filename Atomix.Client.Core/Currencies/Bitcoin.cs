using System;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BlockchainInfo;
using Atomix.Blockchain.Insight;
using Atomix.Blockchain.SoChain;
using Atomix.Wallet.Bip;
using Microsoft.Extensions.Configuration;
using NBitcoin;

namespace Atomix
{
    public class Bitcoin : BitcoinBasedCurrency
    {
        private const long BtcDigitsMultiplier = 100_000_000;

        public Bitcoin()
        {
        }

        public Bitcoin(IConfiguration configuration)
        {
            Name = configuration["Name"];
            Description = configuration["Description"];

            DigitsMultiplier = BtcDigitsMultiplier;
            Digits = (int)Math.Log10(BtcDigitsMultiplier);
            Format = $"F{Digits}";

            FeeRate = decimal.Parse(configuration["FeeRate"]); // 16 satoshi per byte
            FeeDigits = Digits;
            FeeCode = Name;
            FeeFormat = $"F{FeeDigits}";

            HasFeePrice = false;

            Network = ResolveNetwork(configuration);
            BlockchainApi = ResolveBlockchainApi(configuration);
            TxExplorerUri = configuration["TxExplorerUri"];
            AddressExplorerUri = configuration["AddressExplorerUri"];

            IsTransactionsAvailable = true;
            IsSwapAvailable = true;
            Bip44Code = Bip44.Bitcoin;
        }

        private static Network ResolveNetwork(IConfiguration configuration)
        {
            var chain = configuration["Chain"]
                .ToLowerInvariant();

            if (chain.Equals("mainnet"))
                return Network.Main;

            if (chain.Equals("testnet"))
                return Network.TestNet;

            throw new NotSupportedException($"Chain {chain} not supported");
        }

        private IBlockchainApi ResolveBlockchainApi(IConfiguration configuration)
        {
            var blockchainApi = configuration["BlockchainApi"]
                .ToLowerInvariant();

            if (blockchainApi.Equals("blockchaininfo"))
                return new BlockchainInfoApi(this);

            if (blockchainApi.Equals("sochain"))
                return new SoChainApi(this);

            if (blockchainApi.Equals("insight"))
                return new InsightApi(this, configuration);

            throw new NotSupportedException($"BlockchainApi {blockchainApi} not supported");
        }
    }
}