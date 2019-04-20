using System;
using Atomix.Blockchain.BlockchainInfo;
using Atomix.Blockchain.SoChain;
using Atomix.Wallet.Bip;
using NBitcoin;

namespace Atomix
{
    public class Bitcoin : BitcoinBasedCurrency
    {
        public const long BtcDigitsMultiplier = 100_000_000;

        public Bitcoin()
        {
            Name = "BTC";
            Description = "Bitcoin";
            DigitsMultiplier = BtcDigitsMultiplier;
            Digits = (int)Math.Log10(BtcDigitsMultiplier);
            Format = $"F{Digits}";

            FeeRate = 16m; // 16 satoshi per byte
            FeeDigits = Digits;
            FeeCode = Name;
            FeeFormat = $"F{FeeDigits}";

            HasFeePrice = false;           

            Network = Network.TestNet;
            BlockchainApi = new BlockchainInfoApi(this); //new SoChainApi(this);

            IsTransactionsAvailable = true;
            IsSwapAvailable = true;
            Bip44Code = Bip44.Bitcoin;
        }
    }
}