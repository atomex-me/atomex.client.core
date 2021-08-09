using System.Globalization;
using System.Numerics;

using Microsoft.Extensions.Configuration;

using Atomex.Blockchain.Tezos;
using Atomex.Wallet.Bip;

namespace Atomex.TezosTokens
{
    public class Fa2Config : TezosConfig
    {
        public decimal TransferFee { get; private set; }
        public decimal TransferGasLimit { get; private set; }
        public decimal TransferStorageLimit { get; private set; }
        public decimal TransferSize { get; private set; }

        public decimal ApproveFee { get; private set; }
        public decimal ApproveGasLimit { get; private set; }
        public decimal ApproveStorageLimit { get; private set; }
        public decimal ApproveSize { get; private set; }

        public Fa2Config()
        {
        }

        public Fa2Config(IConfiguration configuration)
        {
            Update(configuration);
        }

        public override void Update(IConfiguration configuration)
        {
            Name                    = configuration["Name"];
            Description             = configuration["Description"];

            if (!string.IsNullOrEmpty(configuration["DigitsMultiplier"]))
                DigitsMultiplier = decimal.Parse(configuration["DigitsMultiplier"]);

            DustDigitsMultiplier    = long.Parse(configuration["DustDigitsMultiplier"]);

            Digits = DigitsMultiplier != 0
                ? (int)BigInteger.Log10(new BigInteger(DigitsMultiplier))
                : 0;

            Format                  = $"F{Digits}";
            IsToken                 = bool.Parse(configuration["IsToken"]);

            FeeDigits               = Digits;
            FeeCode                 = "XTZ";
            FeeFormat               = $"F{FeeDigits}";
            HasFeePrice             = false;
            FeeCurrencyName         = "XTZ";

            MaxRewardPercent = configuration[nameof(MaxRewardPercent)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercent)], CultureInfo.InvariantCulture)
                : 0m;
            MaxRewardPercentInBase = configuration[nameof(MaxRewardPercentInBase)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercentInBase)], CultureInfo.InvariantCulture)
                : 0m;
            FeeCurrencyToBaseSymbol = configuration[nameof(FeeCurrencyToBaseSymbol)];
            FeeCurrencySymbol       = configuration[nameof(FeeCurrencySymbol)];

            MinimalFee               = decimal.Parse(configuration[nameof(MinimalFee)], CultureInfo.InvariantCulture);
            MinimalNanotezPerGasUnit = decimal.Parse(configuration[nameof(MinimalNanotezPerGasUnit)], CultureInfo.InvariantCulture);
            MinimalNanotezPerByte    = decimal.Parse(configuration[nameof(MinimalNanotezPerByte)], CultureInfo.InvariantCulture);

            HeadSizeInBytes         = decimal.Parse(configuration[nameof(HeadSizeInBytes)], CultureInfo.InvariantCulture);
            SigSizeInBytes          = decimal.Parse(configuration[nameof(SigSizeInBytes)], CultureInfo.InvariantCulture);

            MicroTezReserve         = decimal.Parse(configuration[nameof(MicroTezReserve)], CultureInfo.InvariantCulture);
            GasReserve              = decimal.Parse(configuration[nameof(GasReserve)], CultureInfo.InvariantCulture);

            MaxFee                  = decimal.Parse(configuration[nameof(MaxFee)], CultureInfo.InvariantCulture);

            RevealFee               = decimal.Parse(configuration[nameof(RevealFee)], CultureInfo.InvariantCulture);
            RevealGasLimit          = decimal.Parse(configuration[nameof(RevealGasLimit)], CultureInfo.InvariantCulture);

            TransferGasLimit        = decimal.Parse(configuration[nameof(TransferGasLimit)], CultureInfo.InvariantCulture);
            TransferStorageLimit    = decimal.Parse(configuration[nameof(TransferStorageLimit)], CultureInfo.InvariantCulture);
            TransferSize            = decimal.Parse(configuration[nameof(TransferSize)], CultureInfo.InvariantCulture);
            TransferFee             = MinimalFee + (TransferGasLimit + GasReserve) * MinimalNanotezPerGasUnit + TransferSize * MinimalNanotezPerByte + 1;

            ApproveGasLimit         = decimal.Parse(configuration[nameof(ApproveGasLimit)], CultureInfo.InvariantCulture);
            ApproveStorageLimit     = decimal.Parse(configuration[nameof(ApproveStorageLimit)], CultureInfo.InvariantCulture);
            ApproveSize             = decimal.Parse(configuration[nameof(ApproveSize)], CultureInfo.InvariantCulture);
            ApproveFee              = MinimalFee + (ApproveGasLimit + GasReserve) * MinimalNanotezPerGasUnit + ApproveSize * MinimalNanotezPerByte + 1;

            InitiateGasLimit        = decimal.Parse(configuration[nameof(InitiateGasLimit)], CultureInfo.InvariantCulture);
            InitiateStorageLimit    = decimal.Parse(configuration[nameof(InitiateStorageLimit)], CultureInfo.InvariantCulture);
            InitiateSize            = decimal.Parse(configuration[nameof(InitiateSize)], CultureInfo.InvariantCulture);
            InitiateFee             = MinimalFee + (InitiateGasLimit + GasReserve) * MinimalNanotezPerGasUnit + InitiateSize * MinimalNanotezPerByte + 1;

            RedeemGasLimit          = decimal.Parse(configuration[nameof(RedeemGasLimit)], CultureInfo.InvariantCulture);
            RedeemStorageLimit      = decimal.Parse(configuration[nameof(RedeemStorageLimit)], CultureInfo.InvariantCulture);
            RedeemSize              = decimal.Parse(configuration[nameof(RedeemSize)], CultureInfo.InvariantCulture);
            RedeemFee               = MinimalFee + (RedeemGasLimit + GasReserve) * MinimalNanotezPerGasUnit + RedeemSize * MinimalNanotezPerByte + 1;

            RefundGasLimit          = decimal.Parse(configuration[nameof(RefundGasLimit)], CultureInfo.InvariantCulture);
            RefundStorageLimit      = decimal.Parse(configuration[nameof(RefundStorageLimit)], CultureInfo.InvariantCulture);
            RefundSize              = decimal.Parse(configuration[nameof(RefundSize)], CultureInfo.InvariantCulture);
            RefundFee               = MinimalFee + (RefundGasLimit + GasReserve) * MinimalNanotezPerGasUnit + RefundStorageLimit * MinimalNanotezPerByte + 1;

            ActivationStorage       = decimal.Parse(configuration[nameof(ActivationStorage)], CultureInfo.InvariantCulture);
            StorageFeeMultiplier    = decimal.Parse(configuration[nameof(StorageFeeMultiplier)], CultureInfo.InvariantCulture);

            BaseUri                 = configuration["BlockchainApiBaseUri"];
            RpcNodeUri              = configuration["BlockchainRpcNodeUri"];
            BbApiUri                = configuration["BbApiUri"];

            BcdApi                  = configuration["BcdApi"];
            BcdNetwork              = configuration["BcdNetwork"];
            BcdSizeLimit = !string.IsNullOrEmpty(configuration["BcdSizeLimit"])
                ? int.Parse(configuration["BcdSizeLimit"])
                : 10;

            BlockchainApi           = ResolveBlockchainApi(configuration, this);
            TxExplorerUri           = configuration["TxExplorerUri"];
            AddressExplorerUri      = configuration["AddressExplorerUri"];
            SwapContractAddress     = configuration["SwapContract"];
            TransactionType         = typeof(TezosTransaction);

            IsSwapAvailable         = false;
            Bip44Code               = Bip44.Tezos; 
        }

        public override decimal GetDefaultFee() =>
            TransferGasLimit;
    }
}