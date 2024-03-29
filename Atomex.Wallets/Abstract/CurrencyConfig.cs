﻿using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Wallets.Bips;

namespace Atomex.Wallets.Abstract
{
    public abstract class CurrencyConfig
    {
        public const decimal MaxRewardForRedeemDeviation = 0.05m;

        public const int StandardKey = 0;
        public const int MaxPrecision = 9;

        public int Id { get; set; }
        public string Name { get; set; }
        public string DisplayedName { get; set; }
        public string Description { get; set; }
        public long DustDigitsMultiplier { get; protected set; }
        public int Decimals { get; set; }
        public int Precision => Decimals <= MaxPrecision ? Decimals : MaxPrecision;
        public string Format { get; set; }
        public bool IsToken { get; set; }
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
        public string BlockchainApi { get; set; }
        public string TxExplorerUri { get; set; }
        public string AddressExplorerUri { get; set; }
        public Type TransactionType { get; protected set; }
        public Type TransactionMetadataType { get; protected set; }
        public bool IsSwapAvailable { get; protected set; }
        public uint Bip44Code { get; protected set; }

        public abstract IBlockchainApi GetBlockchainApi();
        public abstract IExtKey CreateExtKey(SecureBytes seed, int keyType);
        public abstract IKey CreateKey(SecureBytes seed);
        public abstract string AddressFromKey(byte[] publicKey, int keyType);
        public abstract bool IsValidAddress(string address);

        public virtual string GetKeyPathPattern(int keyType) =>
            $"m/{Bip44.Purpose}'/{Bip44Code}'/{{a}}'/{{c}}/{{i}}";

        public abstract Task<Result<BigInteger>> GetPaymentFeeAsync(
            CancellationToken cancellationToken = default);

        public abstract Task<Result<BigInteger>> GetRedeemFeeAsync(
            WalletAddress toAddress = null,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<BigInteger>> GetEstimatedRedeemFeeAsync(
            WalletAddress toAddress = null,
            bool withRewardForRedeem = false,
            CancellationToken cancellationToken = default);

        public abstract Task<Result<decimal>> GetRewardForRedeemAsync(
            decimal maxRewardPercent,
            decimal maxRewardPercentInBase,
            string feeCurrencyToBaseSymbol,
            decimal feeCurrencyToBasePrice,
            string feeCurrencySymbol = null,
            decimal feeCurrencyPrice = 0,
            CancellationToken cancellationToken = default);

        public static decimal CalculateRewardForRedeem(
            decimal redeemFee,
            string redeemFeeCurrency,
            int redeemFeePrecision,
            decimal maxRewardPercent,
            decimal maxRewardPercentValue,
            string feeCurrencyToBaseSymbol,
            decimal feeCurrencyToBasePrice,
            int baseCurrencyPrecision = 2)
        {
            var redeemFeeInBase = AmountHelper.RoundDown(feeCurrencyToBaseSymbol.IsBaseCurrency(redeemFeeCurrency)
                ? redeemFee * feeCurrencyToBasePrice
                : redeemFee / feeCurrencyToBasePrice, baseCurrencyPrecision);

            var k = maxRewardPercentValue / (decimal)Math.Log((double)((1 - maxRewardPercent) / MaxRewardForRedeemDeviation));
            var p = (1 - maxRewardPercent) / (decimal)Math.Exp((double)(redeemFeeInBase / k)) + maxRewardPercent;

            return AmountHelper.RoundDown(redeemFee * (1 + p), redeemFeePrecision);
        }
    }
}