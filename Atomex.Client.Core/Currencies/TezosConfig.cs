using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Common;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Core;
using Atomex.Cryptography;
using Atomex.Wallets;
using Atomex.Wallets.Bips;
using Atomex.Wallets.Keys;

namespace Atomex
{
    public class TezosConfig : CurrencyConfig
    {
        public const string Xtz = "XTZ";
        public const long XtzDigitsMultiplier = 1_000_000;
        public const int HeadOffset = 55;

        // ext key types
        public const int Bip32Ed25519Key = 1;

        private const int PkHashSize = 20;
        protected const int PkHashSizeInBits = PkHashSize * 8;

        public long MinimalFee { get; protected set; }
        public long MinimalNanotezPerGasUnit { get; protected set; }
        public long MinimalNanotezPerByte { get; protected set; }

        public long HeadSizeInBytes { get; protected set; }
        public long SigSizeInBytes { get; protected set; }

        public long MicroTezReserve { get; protected set; }
        public long GasReserve { get; protected set; }

        public long Fee { get; protected set; }
        public long MaxFee { get; protected set; }
        public long GasLimit { get; protected set; }
        public long StorageLimit { get; protected set; }

        public long RevealFee { get; protected set; }
        public long RevealGasLimit { get; protected set; }

        public long InitiateFee { get; protected set; }
        public long InitiateGasLimit { get; protected set; }
        public long InitiateStorageLimit { get; protected set; }
        public long InitiateSize { get; protected set; }

        public long AddFee { get; protected set; }
        public long AddGasLimit { get; protected set; }
        public long AddStorageLimit { get; protected set; }
        public long AddSize { get; protected set; }

        public long RedeemFee { get; protected set; }
        public long RedeemGasLimit { get; protected set; }
        public long RedeemStorageLimit { get; protected set; }
        public long RedeemSize { get; protected set; }

        public long RefundFee { get; protected set; }
        public long RefundGasLimit { get; protected set; }
        public long RefundStorageLimit { get; protected set; }
        public long RefundSize { get; protected set; }

        public long ActivationStorage { get; protected set; }
        public long StorageFeeMultiplier { get; protected set; }

        public string BaseUri { get; protected set; }
        public string RpcNodeUri { get; protected set; }
        public string BbUri { get; protected set; }
        public string BbApiUri { get; protected set; }
        public string SwapContractAddress { get; protected set; }

        public string ThumbsApiUri { get; protected set; }
        public string IpfsGatewayUri { get; protected set; }
        public string CatavaApiUri { get; protected set; }

        public string ChainId { get; set; } = "NetXdQprcVkpaWU";

        public TezosConfig()
        {
        }

        public TezosConfig(IConfiguration configuration)
        {
            Update(configuration);
        }

        public virtual void Update(IConfiguration configuration)
        {
            Name = configuration[nameof(Name)];
            DisplayedName = configuration[nameof(DisplayedName)];
            Description = configuration[nameof(Description)];
            DigitsMultiplier = XtzDigitsMultiplier;
            Digits = (int)Math.Round(Math.Log10(XtzDigitsMultiplier));
            Format = DecimalExtensions.GetFormatWithPrecision(Digits);
            IsToken = bool.Parse(configuration[nameof(IsToken)]);

            FeeCode = Name;
            FeeFormat = DecimalExtensions.GetFormatWithPrecision(Digits);
            HasFeePrice = false;
            FeeCurrencyName = Name;

            MaxRewardPercent = configuration[nameof(MaxRewardPercent)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercent)], CultureInfo.InvariantCulture)
                : 0m;
            MaxRewardPercentInBase = configuration[nameof(MaxRewardPercentInBase)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercentInBase)], CultureInfo.InvariantCulture)
                : 0m;
            FeeCurrencyToBaseSymbol = configuration[nameof(FeeCurrencyToBaseSymbol)];
            FeeCurrencySymbol = configuration[nameof(FeeCurrencySymbol)];

            MinimalFee = long.Parse(configuration[nameof(MinimalFee)], CultureInfo.InvariantCulture);
            MinimalNanotezPerGasUnit = long.Parse(configuration[nameof(MinimalNanotezPerGasUnit)], CultureInfo.InvariantCulture);
            MinimalNanotezPerByte = long.Parse(configuration[nameof(MinimalNanotezPerByte)], CultureInfo.InvariantCulture);

            HeadSizeInBytes = long.Parse(configuration[nameof(HeadSizeInBytes)], CultureInfo.InvariantCulture);
            SigSizeInBytes = long.Parse(configuration[nameof(SigSizeInBytes)], CultureInfo.InvariantCulture);

            MicroTezReserve = long.Parse(configuration[nameof(MicroTezReserve)], CultureInfo.InvariantCulture);
            GasReserve = long.Parse(configuration[nameof(GasReserve)], CultureInfo.InvariantCulture);

            Fee = long.Parse(configuration[nameof(Fee)], CultureInfo.InvariantCulture);
            MaxFee = long.Parse(configuration[nameof(MaxFee)], CultureInfo.InvariantCulture);

            GasLimit = long.Parse(configuration[nameof(GasLimit)], CultureInfo.InvariantCulture);
            StorageLimit = long.Parse(configuration[nameof(StorageLimit)], CultureInfo.InvariantCulture);

            RevealFee = long.Parse(configuration[nameof(RevealFee)], CultureInfo.InvariantCulture);
            RevealGasLimit = long.Parse(configuration[nameof(RevealGasLimit)], CultureInfo.InvariantCulture);

            InitiateGasLimit = long.Parse(configuration[nameof(InitiateGasLimit)], CultureInfo.InvariantCulture);
            InitiateStorageLimit = long.Parse(configuration[nameof(InitiateStorageLimit)], CultureInfo.InvariantCulture);
            InitiateSize = long.Parse(configuration[nameof(InitiateSize)], CultureInfo.InvariantCulture);
            InitiateFee = MinimalFee + (InitiateGasLimit + GasReserve) * MinimalNanotezPerGasUnit + InitiateSize * MinimalNanotezPerByte + 1;

            AddGasLimit = long.Parse(configuration[nameof(AddGasLimit)], CultureInfo.InvariantCulture);
            AddStorageLimit = long.Parse(configuration[nameof(AddStorageLimit)], CultureInfo.InvariantCulture);
            AddSize = long.Parse(configuration[nameof(AddSize)], CultureInfo.InvariantCulture);
            AddFee = MinimalFee + (AddGasLimit + GasReserve) * MinimalNanotezPerGasUnit + AddSize * MinimalNanotezPerByte + 1;

            RedeemGasLimit = long.Parse(configuration[nameof(RedeemGasLimit)], CultureInfo.InvariantCulture);
            RedeemStorageLimit = long.Parse(configuration[nameof(RedeemStorageLimit)], CultureInfo.InvariantCulture);
            RedeemSize = long.Parse(configuration[nameof(RedeemSize)], CultureInfo.InvariantCulture);
            RedeemFee = MinimalFee + (RedeemGasLimit + GasReserve) * MinimalNanotezPerGasUnit + RedeemSize * MinimalNanotezPerByte + 1;

            RefundGasLimit = long.Parse(configuration[nameof(RefundGasLimit)], CultureInfo.InvariantCulture);
            RefundStorageLimit = long.Parse(configuration[nameof(RefundStorageLimit)], CultureInfo.InvariantCulture);
            RefundSize = long.Parse(configuration[nameof(RefundSize)], CultureInfo.InvariantCulture);
            RefundFee = MinimalFee + (RefundGasLimit + GasReserve) * MinimalNanotezPerGasUnit + RefundStorageLimit * MinimalNanotezPerByte + 1;

            ActivationStorage = long.Parse(configuration[nameof(ActivationStorage)], CultureInfo.InvariantCulture);
            StorageFeeMultiplier = long.Parse(configuration[nameof(StorageFeeMultiplier)], CultureInfo.InvariantCulture);

            BaseUri = configuration["BlockchainApiBaseUri"];
            RpcNodeUri = configuration["BlockchainRpcNodeUri"];
            BbUri = configuration[nameof(BbUri)];
            BbApiUri = configuration[nameof(BbApiUri)];

            BlockchainApi = configuration["BlockchainApi"];//ResolveBlockchainApi(configuration);
            TxExplorerUri = configuration[nameof(TxExplorerUri)];
            AddressExplorerUri = configuration[nameof(AddressExplorerUri)];
            SwapContractAddress = configuration["SwapContract"];
            TransactionType = typeof(TezosOperation);

            IsSwapAvailable = true;
            Bip44Code = Bip44.Tezos;

            ThumbsApiUri = configuration[nameof(ThumbsApiUri)];
            CatavaApiUri = configuration[nameof(CatavaApiUri)];
            IpfsGatewayUri = configuration[nameof(IpfsGatewayUri)];
        }

        public override IBlockchainApi GetBlockchainApi() => GetTzktApi();
        public TzktApi GetTzktApi() => new (GetTzktSettings());

        public TzktSettings GetTzktSettings() => new()
        {
            BaseUri = BaseUri,
            Headers = new Dictionary<string, string>
            {
                { "User-Agent", "Atomex" }
            }
        };

        public TezosRpcSettings GetRpcSettings() => new()
        {
            Url = RpcNodeUri,
            ChainId = Blockchain.Tezos.ChainId.Main,
            UserAgent = "Atomex"
        };

        public override IExtKey CreateExtKey(SecureBytes seed, int keyType) =>
            keyType switch
            {
                StandardKey     => new Ed25519ExtKey(seed),
                Bip32Ed25519Key => new Bip32Ed25519ExtKey(seed),
                _               => new Ed25519ExtKey(seed)
            };

        public override IKey CreateKey(SecureBytes seed) =>
            new Ed25519Key(seed);

        public override string AddressFromKey(byte[] publicKey, int keyType) {
            
            return Base58Check.Encode(
                data: new HmacBlake2b(HmacBlake2b.DefaultKeySize, PkHashSize).Mac(key: null, publicKey),
                prefix: TezosPrefix.Tz1);
        }

        public override bool IsValidAddress(string address) =>
            Address.CheckTz1Address(address) ||
            Address.CheckTz2Address(address) ||
            Address.CheckTz3Address(address) ||
            Address.CheckKtAddress(address);

        public override string GetKeyPathPattern(int keyType)
        {
            return keyType switch
            {
                Bip32Ed25519Key => $"m/{Bip44.Purpose}'/{Bip44Code}'/{{a}}'/{{c}}/{{i}}",
                StandardKey or _ => $"m/{Bip44.Purpose}'/{Bip44Code}'/{{i}}'/{{c}}'",
            };
        }

        public override Task<BigInteger> GetPaymentFeeAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BigInteger>(InitiateFee);

        public override Task<BigInteger> GetRedeemFeeAsync(
            WalletAddress toAddress = null,
            CancellationToken cancellationToken = default)
        {
            var activationFee = toAddress == null || toAddress.AvailableBalance() == 0
                ? ActivationStorage * StorageFeeMultiplier
                : 0;

            var result = RedeemFee + RevealFee + MicroTezReserve + activationFee; //todo: define another value for revealed;

            return Task.FromResult<BigInteger>(result);
        }

        public override Task<BigInteger> GetEstimatedRedeemFeeAsync(
            WalletAddress toAddress = null,
            bool withRewardForRedeem = false,
            CancellationToken cancellationToken = default) =>
            GetRedeemFeeAsync(toAddress, cancellationToken);

        public override Task<decimal> GetRewardForRedeemAsync(
            decimal maxRewardPercent,
            decimal maxRewardPercentInBase,
            string feeCurrencyToBaseSymbol,
            decimal feeCurrencyToBasePrice,
            string feeCurrencySymbol = null,
            decimal feeCurrencyPrice = 0,
            CancellationToken cancellationToken = default)
        {
            if (maxRewardPercent == 0 || maxRewardPercentInBase == 0)
                return Task.FromResult(0m);

            var redeemFeeInXtz = (RedeemFee + RevealFee + MicroTezReserve).ToTez();

            return Task.FromResult(CalculateRewardForRedeem(
                redeemFee: redeemFeeInXtz,
                redeemFeeCurrency: Xtz,
                redeemFeeDigitsMultiplier: XtzDigitsMultiplier,
                maxRewardPercent: maxRewardPercent,
                maxRewardPercentValue: maxRewardPercentInBase,
                feeCurrencyToBaseSymbol: feeCurrencyToBaseSymbol,
                feeCurrencyToBasePrice: feeCurrencyToBasePrice));
        }

        public TezosFillOperationSettings GetFillOperationSettings() => new()
        {
            ActivationStorageLimit   = (int)ActivationStorage,
            ChainId                  = ChainId,
            HeadSizeInBytes          = (int)HeadSizeInBytes,
            MinimalFee               = (int)MinimalFee,
            MinimalNanotezPerByte    = MinimalNanotezPerByte,
            MinimalNanotezPerGasUnit = MinimalNanotezPerGasUnit,
            ReserveGasLimit          = (int)GasReserve,
            RevealGasLimit           = (int)RevealGasLimit,
            SignatureSizeInBytes     = (int)SigSizeInBytes
        };
    }
}