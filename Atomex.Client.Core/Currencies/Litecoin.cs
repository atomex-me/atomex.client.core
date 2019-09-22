using System;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Insight;
using Atomex.Blockchain.SoChain;
using Atomex.Wallet.Bip;
using Microsoft.Extensions.Configuration;
using NBitcoin;

namespace Atomex
{
    public class Litecoin : BitcoinBasedCurrency
    {
        private const long LtcDigitsMultiplier = 100_000_000;

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
            Bip44Code = Bip44.Litecoin;
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

            if (blockchainApi.Equals("sochain"))
                return new SoChainApi(this);

            if (blockchainApi.Equals("insight"))
                return new InsightApi(this, configuration);

            throw new NotSupportedException($"BlockchainApi {blockchainApi} not supported");
        }
    }
}