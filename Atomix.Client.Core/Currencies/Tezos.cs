using System;
using Atomix.Core.Entities;
using Atomix.Wallet.Bip;

namespace Atomix
{
    public class Tezos : Currency
    {
        public const long XtzDigitsMultiplier = 100_000_000;
        public const string AlphanetSwapContractAddress = "KT1FU74GimCeEVRAEZGURb6TWU8jK1N6zFJy";

        public string SwapContractAddress { get; protected set; }

        public Tezos()
        {
            Name = "XTZ";
            Description = "Tezos";
            DigitsMultiplier = XtzDigitsMultiplier;
            Digits = (int)Math.Log10(XtzDigitsMultiplier);
            Format = $"F{Digits}";

            //FeeRate = 0; 
            FeeDigits = Digits;
            FeeCode = Name;
            FeeFormat = $"F{FeeDigits}";

            HasFeePrice = false;

            //BlockchainApi = null;
            SwapContractAddress = AlphanetSwapContractAddress;

            IsTransactionsAvailable = false;
            IsSwapAvailable = false;
            Bip44Code = Bip44.Tezos;
        }

        public override string AddressFromKey(byte[] publicKey)
        {
            throw new NotImplementedException();
        }

        public override bool IsValidAddress(string address)
        {
            throw new NotImplementedException();
        }

        public override bool IsAddressFromKey(string address, byte[] publicKey)
        {
            throw new NotImplementedException();
        }

        public override bool VerifyMessage(byte[] publicKey, byte[] data, byte[] signature)
        {
            throw new NotImplementedException();
        }

        public override decimal GetFeeAmount(decimal fee, decimal feePrice)
        {
            throw new NotImplementedException();
        }

        public override decimal GetFeeFromFeeAmount(decimal feeAmount, decimal feePrice)
        {
            throw new NotImplementedException();
        }

        public override decimal GetFeePriceFromFeeAmount(decimal feeAmount, decimal fee)
        {
            throw new NotImplementedException();
        }
    }
}