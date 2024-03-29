﻿using System;
using System.Globalization;
using System.Numerics;

using Microsoft.Extensions.Configuration;

using Atomex.Blockchain;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Wallets.Bips;

namespace Atomex.TezosTokens
{
    public class Fa12Config : TezosTokenConfig
    {
        public override string Standard => TezosHelper.Fa12;
        public decimal GetAllowanceGasLimit { get; private set; }

        public Fa12Config()
        {
        }

        public Fa12Config(IConfiguration configuration)
        {
            Update(configuration);
        }

        public override void Update(IConfiguration configuration)
        {
            Name = configuration[nameof(Name)];
            DisplayedName = configuration[nameof(DisplayedName)];
            Description = configuration[nameof(Description)];

            DustDigitsMultiplier = long.Parse(configuration[nameof(DustDigitsMultiplier)]);
            Decimals = int.Parse(configuration[nameof(Decimals)]);

            Format = DecimalExtensions.GetFormatWithPrecision(Decimals < 9 ? Decimals : 9);
            IsToken = bool.Parse(configuration[nameof(IsToken)]);

            var feeDigits = (int)Math.Round(BigInteger.Log10(new BigInteger(decimal.Parse(configuration["BaseCurrencyDigitsMultiplier"]))));
            FeeFormat = DecimalExtensions.GetFormatWithPrecision(feeDigits);
            HasFeePrice = false;
            FeeCode = "XTZ";
            FeeCurrencyName = "XTZ";

            MaxRewardPercent = configuration[nameof(MaxRewardPercent)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercent)], CultureInfo.InvariantCulture)
                : 0m;
            MaxRewardPercentInBase = configuration[nameof(MaxRewardPercentInBase)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercentInBase)], CultureInfo.InvariantCulture)
                : 0m;
            FeeCurrencyToBaseSymbol = configuration[nameof(FeeCurrencyToBaseSymbol)];
            FeeCurrencySymbol = configuration[nameof(FeeCurrencySymbol)];

            MinimalFee = long.Parse(configuration[nameof(MinimalFee)], CultureInfo.InvariantCulture);
            MinimalNanotezPerGasUnit = decimal.Parse(configuration[nameof(MinimalNanotezPerGasUnit)], CultureInfo.InvariantCulture);
            MinimalNanotezPerByte = decimal.Parse(configuration[nameof(MinimalNanotezPerByte)], CultureInfo.InvariantCulture);

            HeadSizeInBytes = long.Parse(configuration[nameof(HeadSizeInBytes)], CultureInfo.InvariantCulture);
            SigSizeInBytes = long.Parse(configuration[nameof(SigSizeInBytes)], CultureInfo.InvariantCulture);

            MicroTezReserve = long.Parse(configuration[nameof(MicroTezReserve)], CultureInfo.InvariantCulture);
            GasReserve = long.Parse(configuration[nameof(GasReserve)], CultureInfo.InvariantCulture);
            MaxFee = long.Parse(configuration[nameof(MaxFee)], CultureInfo.InvariantCulture);
            StorageLimit = long.Parse(configuration[nameof(StorageLimit)], CultureInfo.InvariantCulture);

            RevealFee = long.Parse(configuration[nameof(RevealFee)], CultureInfo.InvariantCulture);
            RevealGasLimit = long.Parse(configuration[nameof(RevealGasLimit)], CultureInfo.InvariantCulture);

            GetAllowanceGasLimit = decimal.Parse(configuration[nameof(GetAllowanceGasLimit)], CultureInfo.InvariantCulture);

            TransferGasLimit = long.Parse(configuration[nameof(TransferGasLimit)], CultureInfo.InvariantCulture);
            TransferStorageLimit = long.Parse(configuration[nameof(TransferStorageLimit)], CultureInfo.InvariantCulture);
            TransferSize = long.Parse(configuration[nameof(TransferSize)], CultureInfo.InvariantCulture);
            TransferFee = (long)(MinimalFee + (TransferGasLimit + GasReserve) * MinimalNanotezPerGasUnit + TransferSize * MinimalNanotezPerByte + 1);

            ApproveGasLimit = long.Parse(configuration[nameof(ApproveGasLimit)], CultureInfo.InvariantCulture);
            ApproveStorageLimit = long.Parse(configuration[nameof(ApproveStorageLimit)], CultureInfo.InvariantCulture);
            ApproveSize = long.Parse(configuration[nameof(ApproveSize)], CultureInfo.InvariantCulture);
            ApproveFee = (long)(MinimalFee + (ApproveGasLimit + GasReserve) * MinimalNanotezPerGasUnit + ApproveSize * MinimalNanotezPerByte + 1);

            InitiateGasLimit = long.Parse(configuration[nameof(InitiateGasLimit)], CultureInfo.InvariantCulture);
            InitiateStorageLimit = long.Parse(configuration[nameof(InitiateStorageLimit)], CultureInfo.InvariantCulture);
            InitiateSize = long.Parse(configuration[nameof(InitiateSize)], CultureInfo.InvariantCulture);
            InitiateFee = (long)(MinimalFee + (InitiateGasLimit + GasReserve) * MinimalNanotezPerGasUnit + InitiateSize * MinimalNanotezPerByte + 1);

            RedeemGasLimit = long.Parse(configuration[nameof(RedeemGasLimit)], CultureInfo.InvariantCulture);
            RedeemStorageLimit = long.Parse(configuration[nameof(RedeemStorageLimit)], CultureInfo.InvariantCulture);
            RedeemSize = long.Parse(configuration[nameof(RedeemSize)], CultureInfo.InvariantCulture);
            RedeemFee = (long)(MinimalFee + (RedeemGasLimit + GasReserve) * MinimalNanotezPerGasUnit + RedeemSize * MinimalNanotezPerByte + 1);

            RefundGasLimit = long.Parse(configuration[nameof(RefundGasLimit)], CultureInfo.InvariantCulture);
            RefundStorageLimit = long.Parse(configuration[nameof(RefundStorageLimit)], CultureInfo.InvariantCulture);
            RefundSize = long.Parse(configuration[nameof(RefundSize)], CultureInfo.InvariantCulture);
            RefundFee = (long)(MinimalFee + (RefundGasLimit + GasReserve) * MinimalNanotezPerGasUnit + RefundStorageLimit * MinimalNanotezPerByte + 1);

            ActivationStorage = long.Parse(configuration[nameof(ActivationStorage)], CultureInfo.InvariantCulture);
            StorageFeeMultiplier = long.Parse(configuration[nameof(StorageFeeMultiplier)], CultureInfo.InvariantCulture);

            BaseUri = configuration["BlockchainApiBaseUri"];
            RpcNodeUri = configuration["BlockchainRpcNodeUri"];
            BbApiUri = configuration[nameof(BbApiUri)];

            BlockchainApi = configuration["BlockchainApi"];
            TxExplorerUri = configuration[nameof(TxExplorerUri)];
            AddressExplorerUri = configuration[nameof(AddressExplorerUri)];
            SwapContractAddress = configuration["SwapContract"];
            TokenContractAddress = configuration["TokenContract"];
            TokenId = 0;

            ViewContractAddress = configuration["ViewContract"];
            TransactionType = typeof(TezosTokenTransfer);
            TransactionMetadataType = typeof(TransactionMetadata);

            IsSwapAvailable = true;
            Bip44Code = Bip44.Tezos;
        }
    }
}