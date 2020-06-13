using System;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Cryptography;

namespace Atomex.Core
{
    public abstract class Currency
    {
        public const int MaxNameLength = 32;
        public const string CoinsDefaultFileName = "coins.default.json";

        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public long DigitsMultiplier { get; protected set; }
        public long DustDigitsMultiplier { get; protected set; }
        public int Digits { get; set; }
        public string Format { get; set; }

        public int FeeDigits { get; set; }
        public string FeeCode { get; set; }
        public string FeeFormat { get; set; }
        public bool HasFeePrice { get; set; }
        public string FeePriceCode { get; set; }
        public string FeePriceFormat { get; set; }
        public string FeeCurrencyName { get; set; }

        public IBlockchainApi BlockchainApi { get; set; }
        public string TxExplorerUri { get; set; }
        public string AddressExplorerUri { get; set; }
        public Type TransactionType { get; protected set; }

        public bool IsTransactionsAvailable { get; protected set; }
        public bool IsSwapAvailable { get; protected set; }
        public uint Bip44Code { get; protected set; }

        public abstract IExtKey CreateExtKey(SecureBytes seed);

        public abstract IKey CreateKey(SecureBytes seed);

        public abstract string AddressFromKey(byte[] publicKey);

        public abstract bool IsValidAddress(string address);

        public abstract bool IsAddressFromKey(string address, byte[] publicKey);

        public abstract bool VerifyMessage(byte[] data, byte[] signature, byte[] publicKey);

        public abstract decimal GetFeeAmount(decimal fee, decimal feePrice);

        public abstract decimal GetFeeFromFeeAmount(decimal feeAmount, decimal feePrice);

        public abstract decimal GetFeePriceFromFeeAmount(decimal feeAmount, decimal fee);

        public abstract decimal GetRedeemFee(WalletAddress toAddress = null);

        public abstract decimal GetRewardForRedeem();

        public virtual decimal GetDefaultFeePrice()
        {
            return 1m;
        }
        public virtual decimal GetMaximumFee()
        {
            return decimal.MaxValue;
        }
    }
}