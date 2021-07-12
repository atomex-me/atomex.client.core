using System;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Cryptography;

namespace Atomex.Core
{
    public abstract class Currency
    {
        public const decimal MaxRewardForRedeemDeviation = 0.05m;

        public const int MaxNameLength = 32;
        public const string CoinsDefaultFileName = "coins.default.json";

        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal DigitsMultiplier { get; protected set; }
        public long DustDigitsMultiplier { get; protected set; }
        public int Digits { get; set; }
        public string Format { get; set; }
        public bool IsToken { get; set; }

        public int FeeDigits { get; set; }
        public string FeeCode { get; set; }
        public string FeeFormat { get; set; }
        public bool HasFeePrice { get; set; }
        public string FeePriceCode { get; set; }
        public string FeePriceFormat { get; set; }
        public string FeeCurrencyName { get; set; }

        public decimal MaxRewardPercent { get; set; }
        public decimal MaxRewardPercentInBase { get; set; }
        public string FeeCurrencyToBaseSymbol { get; set; }
        public string FeeCurrencySymbol { get; set; }


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

        public abstract Task<decimal> GetPaymentFeeAsync(
            CancellationToken cancellationToken = default);

        public abstract Task<decimal> GetRedeemFeeAsync(
            WalletAddress toAddress = null,
            CancellationToken cancellationToken = default);

        public abstract Task<decimal> GetEstimatedRedeemFeeAsync(
            WalletAddress toAddress = null,
            bool withRewardForRedeem = false,
            CancellationToken cancellationToken = default);

        public abstract Task<decimal> GetRewardForRedeemAsync(
            decimal maxRewardPercent,
            decimal maxRewardPercentInBase,
            string feeCurrencyToBaseSymbol,
            decimal feeCurrencyToBasePrice,
            string feeCurrencySymbol = null,
            decimal feeCurrencyPrice = 0,
            CancellationToken cancellationToken = default);

        public virtual Task<decimal> GetDefaultFeePriceAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(1m);

        public virtual decimal GetDefaultFee() =>
            1m;

        public virtual decimal GetMaximumFee() =>
            decimal.MaxValue;

        public static decimal CalculateRewardForRedeem(
            decimal redeemFee,
            string redeemFeeCurrency,
            decimal redeemFeeDigitsMultiplier,
            decimal maxRewardPercent,
            decimal maxRewardPercentValue,
            string feeCurrencyToBaseSymbol,
            decimal feeCurrencyToBasePrice,
            decimal baseDigitsMultiplier = 2)
        {
            var redeemFeeInBase = AmountHelper.RoundDown(feeCurrencyToBaseSymbol.IsBaseCurrency(redeemFeeCurrency)
                ? redeemFee * feeCurrencyToBasePrice
                : redeemFee / feeCurrencyToBasePrice, baseDigitsMultiplier);

            var k = maxRewardPercentValue / (decimal)Math.Log((double)((1 - maxRewardPercent) / MaxRewardForRedeemDeviation));
            var p = (1 - maxRewardPercent) / (decimal)Math.Exp((double)(redeemFeeInBase / k)) + maxRewardPercent;

            return AmountHelper.RoundDown(redeemFee * (1 + p), redeemFeeDigitsMultiplier);
        }
    }
}