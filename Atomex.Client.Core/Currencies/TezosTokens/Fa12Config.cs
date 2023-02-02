using System;
using System.Globalization;
using System.Numerics;

using Microsoft.Extensions.Configuration;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Wallet.Bip;

namespace Atomex.TezosTokens
{
    public class Fa12Config : TezosTokenConfig
    {
        public decimal GetBalanceFee { get; private set; }
        public decimal GetBalanceGasLimit { get; private set; }
        public decimal GetBalanceStorageLimit { get; private set; }
        public decimal GetBalanceSize { get; private set; }

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
            Name                    = configuration[nameof(Name)];
            DisplayedName           = configuration[nameof(DisplayedName)];
            Description             = configuration[nameof(Description)];

            if (!string.IsNullOrEmpty(configuration[nameof(DigitsMultiplier)]))
                DigitsMultiplier = decimal.Parse(configuration[nameof(DigitsMultiplier)]);

            DustDigitsMultiplier    = long.Parse(configuration[nameof(DustDigitsMultiplier)]);
            
            Digits = DigitsMultiplier != 0
                ? (int)Math.Round(BigInteger.Log10(new BigInteger(DigitsMultiplier)))
                : 0;

            Format                  = DecimalExtensions.GetFormatWithPrecision(Digits < 9 ? Digits : 9);
            IsToken                 = bool.Parse(configuration[nameof(IsToken)]);

            var feeDigits           = (int)Math.Round(BigInteger.Log10(new BigInteger(decimal.Parse(configuration["BaseCurrencyDigitsMultiplier"]))));
            FeeFormat               = DecimalExtensions.GetFormatWithPrecision(feeDigits);
            HasFeePrice             = false;
            FeeCode                 = "XTZ";
            FeeCurrencyName         = "XTZ";

            MaxRewardPercent        = configuration[nameof(MaxRewardPercent)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercent)], CultureInfo.InvariantCulture)
                : 0m;
            MaxRewardPercentInBase  = configuration[nameof(MaxRewardPercentInBase)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercentInBase)], CultureInfo.InvariantCulture)
                : 0m;
            FeeCurrencyToBaseSymbol = configuration[nameof(FeeCurrencyToBaseSymbol)];
            FeeCurrencySymbol       = configuration[nameof(FeeCurrencySymbol)];

            MinimalFee               = long.Parse(configuration[nameof(MinimalFee)], CultureInfo.InvariantCulture);
            MinimalNanotezPerGasUnit = long.Parse(configuration[nameof(MinimalNanotezPerGasUnit)], CultureInfo.InvariantCulture);
            MinimalNanotezPerByte    = long.Parse(configuration[nameof(MinimalNanotezPerByte)], CultureInfo.InvariantCulture);

            HeadSizeInBytes         = long.Parse(configuration[nameof(HeadSizeInBytes)], CultureInfo.InvariantCulture);
            SigSizeInBytes          = long.Parse(configuration[nameof(SigSizeInBytes)], CultureInfo.InvariantCulture);

            MicroTezReserve         = long.Parse(configuration[nameof(MicroTezReserve)], CultureInfo.InvariantCulture);
            GasReserve              = long.Parse(configuration[nameof(GasReserve)], CultureInfo.InvariantCulture);
            MaxFee                  = long.Parse(configuration[nameof(MaxFee)], CultureInfo.InvariantCulture);
            StorageLimit            = long.Parse(configuration[nameof(StorageLimit)], CultureInfo.InvariantCulture);

            RevealFee               = long.Parse(configuration[nameof(RevealFee)], CultureInfo.InvariantCulture);
            RevealGasLimit          = long.Parse(configuration[nameof(RevealGasLimit)], CultureInfo.InvariantCulture);

            GetAllowanceGasLimit    = decimal.Parse(configuration[nameof(GetAllowanceGasLimit)], CultureInfo.InvariantCulture);

            TransferGasLimit        = long.Parse(configuration[nameof(TransferGasLimit)], CultureInfo.InvariantCulture);
            TransferStorageLimit    = long.Parse(configuration[nameof(TransferStorageLimit)], CultureInfo.InvariantCulture);
            TransferSize            = long.Parse(configuration[nameof(TransferSize)], CultureInfo.InvariantCulture);
            TransferFee             = MinimalFee + (TransferGasLimit + GasReserve) * MinimalNanotezPerGasUnit + TransferSize * MinimalNanotezPerByte + 1;

            ApproveGasLimit         = long.Parse(configuration[nameof(ApproveGasLimit)], CultureInfo.InvariantCulture);
            ApproveStorageLimit     = long.Parse(configuration[nameof(ApproveStorageLimit)], CultureInfo.InvariantCulture);
            ApproveSize             = long.Parse(configuration[nameof(ApproveSize)], CultureInfo.InvariantCulture);
            ApproveFee              = MinimalFee + (ApproveGasLimit + GasReserve) * MinimalNanotezPerGasUnit + ApproveSize * MinimalNanotezPerByte + 1;

            InitiateGasLimit        = long.Parse(configuration[nameof(InitiateGasLimit)], CultureInfo.InvariantCulture);
            InitiateStorageLimit    = long.Parse(configuration[nameof(InitiateStorageLimit)], CultureInfo.InvariantCulture);
            InitiateSize            = long.Parse(configuration[nameof(InitiateSize)], CultureInfo.InvariantCulture);
            InitiateFee             = MinimalFee + (InitiateGasLimit + GasReserve) * MinimalNanotezPerGasUnit + InitiateSize * MinimalNanotezPerByte + 1;

            RedeemGasLimit          = long.Parse(configuration[nameof(RedeemGasLimit)], CultureInfo.InvariantCulture);
            RedeemStorageLimit      = long.Parse(configuration[nameof(RedeemStorageLimit)], CultureInfo.InvariantCulture);
            RedeemSize              = long.Parse(configuration[nameof(RedeemSize)], CultureInfo.InvariantCulture);
            RedeemFee               = MinimalFee + (RedeemGasLimit + GasReserve) * MinimalNanotezPerGasUnit + RedeemSize * MinimalNanotezPerByte + 1;

            RefundGasLimit          = long.Parse(configuration[nameof(RefundGasLimit)], CultureInfo.InvariantCulture);
            RefundStorageLimit      = long.Parse(configuration[nameof(RefundStorageLimit)], CultureInfo.InvariantCulture);
            RefundSize              = long.Parse(configuration[nameof(RefundSize)], CultureInfo.InvariantCulture);
            RefundFee               = MinimalFee + (RefundGasLimit + GasReserve) * MinimalNanotezPerGasUnit + RefundStorageLimit * MinimalNanotezPerByte + 1;

            ActivationStorage       = long.Parse(configuration[nameof(ActivationStorage)], CultureInfo.InvariantCulture);
            StorageFeeMultiplier    = long.Parse(configuration[nameof(StorageFeeMultiplier)], CultureInfo.InvariantCulture);

            BaseUri                 = configuration["BlockchainApiBaseUri"];
            RpcNodeUri              = configuration["BlockchainRpcNodeUri"];
            BbApiUri                = configuration[nameof(BbApiUri)];

            BlockchainApi = configuration["BlockchainApi"];
            TxExplorerUri           = configuration[nameof(TxExplorerUri)];
            AddressExplorerUri      = configuration[nameof(AddressExplorerUri)];
            SwapContractAddress     = configuration["SwapContract"];
            TokenContractAddress    = configuration["TokenContract"];
            TokenId                 = 0;

            ViewContractAddress     = configuration["ViewContract"];
            TransactionType         = typeof(TezosOperation);

            IsSwapAvailable         = true;
            Bip44Code               = Bip44.Tezos;
        }
    }
}