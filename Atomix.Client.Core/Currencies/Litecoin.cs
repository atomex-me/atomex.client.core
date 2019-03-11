using System;
using Atomix.Blockchain.SoChain;
using Atomix.Wallet.Bip;
using NBitcoin.Altcoins;

namespace Atomix
{
    public class Litecoin : BitcoinBasedCurrency
    {
        public const long LtcDigitsMultiplier = 100_000_000;

        public Litecoin()
        {
            Name = "LTC";
            Description = "Litecoin";
            DigitsMultiplier = LtcDigitsMultiplier;
            Digits = (int)Math.Log10(LtcDigitsMultiplier);
            Format = $"F{Digits}";

            FeeRate = 100m; // 100 litoshi per byte ~ 0.001 LTC/Bb
            FeeDigits = Digits;
            FeeCode = Name;
            FeeFormat = $"F{FeeDigits}";

            HasFeePrice = false;

            Network = AltNetworkSets.Litecoin.Testnet;    
            BlockchainApi = new SoChainApi(this);
            
            IsTransactionsAvailable = true;
            IsSwapAvailable = true;
            Bip44Code = Bip44.Litecoin;
        }
    }
}