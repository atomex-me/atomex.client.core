using System;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitCore;
using Atomex.Blockchain.Insight;
using Atomex.Wallet.Bip;
using Microsoft.Extensions.Configuration;
using NBitcoin;

namespace Atomex
{
    public class Bitcoin : BitcoinBasedCurrency
    {
        private const long BtcDigitsMultiplier = 100_000_000;

        public Bitcoin()
        {
        }

        public Bitcoin(IConfiguration configuration)
        {
            Update(configuration);
        }

        public void Update(IConfiguration configuration)
        {
            Name = configuration["Name"];
            Description = configuration["Description"];

            DigitsMultiplier = BtcDigitsMultiplier;
            Digits = (int)Math.Log10(BtcDigitsMultiplier);
            Format = $"F{Digits}";

            FeeRate = decimal.Parse(configuration["FeeRate"]);
            DustFeeRate = decimal.Parse(configuration["DustFeeRate"]);
            MinTxFeeRate = decimal.Parse(configuration["MinTxFeeRate"]);
            MinRelayTxFeeRate = decimal.Parse(configuration["MinRelayTxFeeRate"]);

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

            if (blockchainApi.Equals("insight"))
                return new InsightApi(this, configuration);

            if (blockchainApi.Equals("bitcore+blockcypher"))
                return new BitCoreApi(this, configuration);

            throw new NotSupportedException($"BlockchainApi {blockchainApi} not supported");
        }
    }
}