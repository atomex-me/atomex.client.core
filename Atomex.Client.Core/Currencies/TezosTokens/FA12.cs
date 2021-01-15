using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Wallet.Bip;

namespace Atomex.TezosTokens
{
    public class FA12 : Tezos
    {
        public decimal GetBalanceFee { get; private set; }
        public decimal GetBalanceGasLimit { get; private set; }
        public decimal GetBalanceStorageLimit { get; private set; }
        public decimal GetBalanceSize { get; private set; }

        public decimal GetAllowanceFee { get; private set; }
        public decimal GetAllowanceGasLimit { get; private set; }
        public decimal GetAllowanceStorageLimit { get; private set; }
        public decimal GetAllowanceSize { get; private set; }


        public decimal TransferFee { get; private set; }
        public decimal TransferGasLimit { get; private set; }
        public decimal TransferStorageLimit { get; private set; }
        public decimal TransferSize { get; private set; }

        public decimal ApproveFee { get; private set; }
        public decimal ApproveGasLimit { get; private set; }
        public decimal ApproveStorageLimit { get; private set; }
        public decimal ApproveSize { get; private set; }

        public string TokenContractAddress { get; private set; }
        public string ViewContractAddress { get; private set; }
        public string BcdApi { get; private set; }
        public string BcdNetwork { get; private set; }

        public FA12()
        {
        }

        public FA12(IConfiguration configuration)
        {
            Update(configuration);
        }

        public override void Update(IConfiguration configuration)
        {
            Name = configuration["Name"];
            Description = configuration["Description"];
            DigitsMultiplier = decimal.Parse(configuration["DigitsMultiplier"]);
            DustDigitsMultiplier = long.Parse(configuration["DustDigitsMultiplier"]);
            Digits = (int)BigInteger.Log10(new BigInteger(DigitsMultiplier));
            Format = $"F{Digits}";

            FeeDigits = Digits;
            FeeCode = "XTZ";
            FeeFormat = $"F{FeeDigits}";
            HasFeePrice = false;
            FeeCurrencyName = "XTZ";

            MinimalFee = decimal.Parse(configuration[nameof(MinimalFee)], CultureInfo.InvariantCulture);
            MinimalNanotezPerGasUnit = decimal.Parse(configuration[nameof(MinimalNanotezPerGasUnit)], CultureInfo.InvariantCulture);
            MinimalNanotezPerByte = decimal.Parse(configuration[nameof(MinimalNanotezPerByte)], CultureInfo.InvariantCulture);

            HeadSizeInBytes = decimal.Parse(configuration[nameof(HeadSizeInBytes)], CultureInfo.InvariantCulture);
            SigSizeInBytes = decimal.Parse(configuration[nameof(SigSizeInBytes)], CultureInfo.InvariantCulture);

            MicroTezReserve = decimal.Parse(configuration[nameof(MicroTezReserve)], CultureInfo.InvariantCulture);
            GasReserve = decimal.Parse(configuration[nameof(GasReserve)], CultureInfo.InvariantCulture);

            MaxFee = decimal.Parse(configuration[nameof(MaxFee)], CultureInfo.InvariantCulture);

            GasLimit = decimal.Parse(configuration[nameof(GasLimit)], CultureInfo.InvariantCulture);
            StorageLimit = decimal.Parse(configuration[nameof(StorageLimit)], CultureInfo.InvariantCulture);

            RevealFee = decimal.Parse(configuration[nameof(RevealFee)], CultureInfo.InvariantCulture);
            RevealGasLimit = decimal.Parse(configuration[nameof(RevealGasLimit)], CultureInfo.InvariantCulture);

            GetBalanceGasLimit = decimal.Parse(configuration[nameof(GetBalanceGasLimit)], CultureInfo.InvariantCulture);
            GetBalanceStorageLimit = decimal.Parse(configuration[nameof(GetBalanceStorageLimit)], CultureInfo.InvariantCulture);
            GetBalanceSize = decimal.Parse(configuration[nameof(GetBalanceSize)], CultureInfo.InvariantCulture);
            GetBalanceFee = MinimalFee + (GetBalanceGasLimit + GasReserve) * MinimalNanotezPerGasUnit + GetBalanceSize * MinimalNanotezPerByte + 1;

            GetAllowanceGasLimit = decimal.Parse(configuration[nameof(GetAllowanceGasLimit)], CultureInfo.InvariantCulture);
            //GetAllowanceStorageLimit = decimal.Parse(configuration[nameof(GetAllowanceStorageLimit)], CultureInfo.InvariantCulture);
            //GetAllowanceSize = decimal.Parse(configuration[nameof(GetAllowanceSize)], CultureInfo.InvariantCulture);
            //GetAllowanceFee = MinimalFee + (GetAllowanceGasLimit + GasReserve) * MinimalNanotezPerGasUnit + GetAllowanceSize * MinimalNanotezPerByte + 1;

            TransferGasLimit = decimal.Parse(configuration[nameof(TransferGasLimit)], CultureInfo.InvariantCulture);
            TransferStorageLimit = decimal.Parse(configuration[nameof(TransferStorageLimit)], CultureInfo.InvariantCulture);
            TransferSize = decimal.Parse(configuration[nameof(TransferSize)], CultureInfo.InvariantCulture);
            TransferFee = MinimalFee + (TransferGasLimit + GasReserve) * MinimalNanotezPerGasUnit + TransferSize * MinimalNanotezPerByte + 1;

            ApproveGasLimit = decimal.Parse(configuration[nameof(ApproveGasLimit)], CultureInfo.InvariantCulture);
            ApproveStorageLimit = decimal.Parse(configuration[nameof(ApproveStorageLimit)], CultureInfo.InvariantCulture);
            ApproveSize = decimal.Parse(configuration[nameof(ApproveSize)], CultureInfo.InvariantCulture);
            ApproveFee = MinimalFee + (ApproveGasLimit + GasReserve) * MinimalNanotezPerGasUnit + ApproveSize * MinimalNanotezPerByte + 1;

            AddGasLimit = decimal.Parse(configuration[nameof(AddGasLimit)], CultureInfo.InvariantCulture);
            AddStorageLimit = decimal.Parse(configuration[nameof(AddStorageLimit)], CultureInfo.InvariantCulture);
            AddSize = decimal.Parse(configuration[nameof(AddSize)], CultureInfo.InvariantCulture);
            AddFee = MinimalFee + (AddGasLimit + GasReserve) * MinimalNanotezPerGasUnit + AddSize * MinimalNanotezPerByte + 1;

            InitiateGasLimit = decimal.Parse(configuration[nameof(InitiateGasLimit)], CultureInfo.InvariantCulture);
            InitiateStorageLimit = decimal.Parse(configuration[nameof(InitiateStorageLimit)], CultureInfo.InvariantCulture);
            InitiateSize = decimal.Parse(configuration[nameof(InitiateSize)], CultureInfo.InvariantCulture);
            InitiateFee = MinimalFee + (InitiateGasLimit + GasReserve) * MinimalNanotezPerGasUnit + InitiateSize * MinimalNanotezPerByte + 1;

            AddGasLimit = decimal.Parse(configuration[nameof(AddGasLimit)], CultureInfo.InvariantCulture);
            AddStorageLimit = decimal.Parse(configuration[nameof(AddStorageLimit)], CultureInfo.InvariantCulture);
            AddSize = decimal.Parse(configuration[nameof(AddSize)], CultureInfo.InvariantCulture);
            AddFee = MinimalFee + (AddGasLimit + GasReserve) * MinimalNanotezPerGasUnit + AddSize * MinimalNanotezPerByte + 1;

            RedeemGasLimit = decimal.Parse(configuration[nameof(RedeemGasLimit)], CultureInfo.InvariantCulture);
            RedeemStorageLimit = decimal.Parse(configuration[nameof(RedeemStorageLimit)], CultureInfo.InvariantCulture);
            RedeemSize = decimal.Parse(configuration[nameof(RedeemSize)], CultureInfo.InvariantCulture);
            RedeemFee = MinimalFee + (RedeemGasLimit + GasReserve) * MinimalNanotezPerGasUnit + RedeemSize * MinimalNanotezPerByte + 1;

            RefundGasLimit = decimal.Parse(configuration[nameof(RefundGasLimit)], CultureInfo.InvariantCulture);
            RefundStorageLimit = decimal.Parse(configuration[nameof(RefundStorageLimit)], CultureInfo.InvariantCulture);
            RefundSize = decimal.Parse(configuration[nameof(RefundSize)], CultureInfo.InvariantCulture);
            RefundFee = MinimalFee + (RefundGasLimit + GasReserve) * MinimalNanotezPerGasUnit + RefundStorageLimit * MinimalNanotezPerByte + 1;

            ActivationStorage = decimal.Parse(configuration[nameof(ActivationStorage)], CultureInfo.InvariantCulture);
            StorageFeeMultiplier = decimal.Parse(configuration[nameof(StorageFeeMultiplier)], CultureInfo.InvariantCulture);

            BaseUri = configuration["BlockchainApiBaseUri"];
            RpcNodeUri = configuration["BlockchainRpcNodeUri"];
            BbApiUri = configuration["BbApiUri"];
            BcdApi = configuration["BcdApi"];
            BcdNetwork = configuration["BcdNetwork"];

            BlockchainApi = ResolveBlockchainApi(configuration, this);
            TxExplorerUri = configuration["TxExplorerUri"];
            AddressExplorerUri = configuration["AddressExplorerUri"];
            SwapContractAddress = configuration["SwapContract"];
            TokenContractAddress = configuration["TokenContract"];
            ViewContractAddress = configuration["ViewContract"];
            TransactionType = typeof(TezosTransaction);

            IsTransactionsAvailable = true;
            IsSwapAvailable = true;
            Bip44Code = Bip44.Tezos;
        }

        public override async Task<decimal> GetRewardForRedeemAsync(
            string chainCurrencySymbol = null,
            decimal chainCurrencyPrice = 0,
            string baseCurrencySymbol = null,
            decimal baseCurrencyPrice = 0,
            CancellationToken cancellationToken = default)
        {
            var rewardForRedeemInXtz = await base.GetRewardForRedeemAsync(
                chainCurrencySymbol: chainCurrencySymbol,
                chainCurrencyPrice: chainCurrencyPrice,
                baseCurrencySymbol: baseCurrencySymbol,
                baseCurrencyPrice: baseCurrencyPrice,
                cancellationToken: cancellationToken);

            if (chainCurrencySymbol == null || chainCurrencyPrice == 0)
                return 0m;

            return AmountHelper.RoundDown(chainCurrencySymbol.IsBaseCurrency(Name)
                ? rewardForRedeemInXtz / chainCurrencyPrice
                : rewardForRedeemInXtz * chainCurrencyPrice, DigitsMultiplier);
        }

        public override decimal GetDefaultFee() =>
            TransferGasLimit;
    }

    public class TZBTC : FA12
    {
        public TZBTC()
        {
        }

        public TZBTC(IConfiguration configuration)
            : base(configuration)
        {
        }
    }
}