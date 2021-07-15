using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography;
using Atomex.Wallet.Bip;
using Atomex.Wallet.Tezos;

namespace Atomex
{
    public class TezosConfig : CurrencyConfig
    {
        public const string Xtz = "XTZ";
        public const long XtzDigitsMultiplier = 1_000_000;
        public const int HeadOffset = 55;

        protected const int PkHashSize = 20 * 8;

        public decimal MinimalFee { get; protected set; }
        public decimal MinimalNanotezPerGasUnit { get; protected set; }
        public decimal MinimalNanotezPerByte { get; protected set; }

        public decimal HeadSizeInBytes { get; protected set; }
        public decimal SigSizeInBytes { get; protected set; }

        public decimal MicroTezReserve { get; protected set; }
        public decimal GasReserve { get; protected set; }

        public decimal Fee { get; protected set; }
        public decimal MaxFee { get; protected set; }
        public decimal GasLimit { get; protected set; }
        public decimal StorageLimit { get; protected set; }

        public decimal RevealFee { get; protected set; }
        public decimal RevealGasLimit { get; protected set; }

        public decimal InitiateFee { get; protected set; }
        public decimal InitiateGasLimit { get; protected set; }
        public decimal InitiateStorageLimit { get; protected set; }
        public decimal InitiateSize { get; protected set; }

        public decimal AddFee { get; protected set; }
        public decimal AddGasLimit { get; protected set; }
        public decimal AddStorageLimit { get; protected set; }
        public decimal AddSize { get; protected set; }

        public decimal RedeemFee { get; protected set; }
        public decimal RedeemGasLimit { get; protected set; }
        public decimal RedeemStorageLimit { get; protected set; }
        public decimal RedeemSize { get; protected set; }

        public decimal RefundFee { get; protected set; }
        public decimal RefundGasLimit { get; protected set; }
        public decimal RefundStorageLimit { get; protected set; }
        public decimal RefundSize { get; protected set; }

        public decimal ActivationStorage { get; protected set; }
        public decimal StorageFeeMultiplier { get; protected set; }

        public string BaseUri { get; protected set; }
        public string RpcNodeUri { get; protected set; }
        public string BbUri { get; protected set; }
        public string BbApiUri { get; protected set; }
        public string SwapContractAddress { get; protected set; }

        public string BcdApi { get; protected set; }
        public string BcdNetwork { get; protected set; }
        public int BcdSizeLimit { get; protected set; }

        public TezosConfig()
        {
        }

        public TezosConfig(IConfiguration configuration)
        {
            Update(configuration);
        }

        public virtual void Update(IConfiguration configuration)
        {
            Name                    = configuration["Name"];
            Description             = configuration["Description"];
            DigitsMultiplier        = XtzDigitsMultiplier;
            Digits                  = (int)Math.Log10(XtzDigitsMultiplier);
            Format                  = $"F{Digits}";
            IsToken                 = bool.Parse(configuration["IsToken"]);

            FeeDigits               = Digits;
            FeeCode                 = Name;
            FeeFormat               = $"F{FeeDigits}";
            HasFeePrice             = false;
            FeeCurrencyName         = Name;

            MaxRewardPercent        = configuration[nameof(MaxRewardPercent)] != null
                ? decimal.Parse(configuration[nameof(MaxRewardPercent)], CultureInfo.InvariantCulture)
                : 0m;
            MaxRewardPercentInBase  = configuration[nameof(MaxRewardPercentInBase)] != null
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

            Fee                     = decimal.Parse(configuration[nameof(Fee)], CultureInfo.InvariantCulture);
            MaxFee                  = decimal.Parse(configuration[nameof(MaxFee)], CultureInfo.InvariantCulture);

            GasLimit                = decimal.Parse(configuration[nameof(GasLimit)], CultureInfo.InvariantCulture);
            StorageLimit            = decimal.Parse(configuration[nameof(StorageLimit)], CultureInfo.InvariantCulture);

            RevealFee               = decimal.Parse(configuration[nameof(RevealFee)], CultureInfo.InvariantCulture);
            RevealGasLimit          = decimal.Parse(configuration[nameof(RevealGasLimit)], CultureInfo.InvariantCulture);

            InitiateGasLimit        = decimal.Parse(configuration[nameof(InitiateGasLimit)], CultureInfo.InvariantCulture);
            InitiateStorageLimit    = decimal.Parse(configuration[nameof(InitiateStorageLimit)], CultureInfo.InvariantCulture);
            InitiateSize            = decimal.Parse(configuration[nameof(InitiateSize)], CultureInfo.InvariantCulture);
            InitiateFee             = MinimalFee + (InitiateGasLimit + GasReserve) * MinimalNanotezPerGasUnit + InitiateSize * MinimalNanotezPerByte + 1;

            AddGasLimit             = decimal.Parse(configuration[nameof(AddGasLimit)], CultureInfo.InvariantCulture);
            AddStorageLimit         = decimal.Parse(configuration[nameof(AddStorageLimit)], CultureInfo.InvariantCulture);
            AddSize                 = decimal.Parse(configuration[nameof(AddSize)], CultureInfo.InvariantCulture);
            AddFee                  = MinimalFee + (AddGasLimit + GasReserve) * MinimalNanotezPerGasUnit + AddSize * MinimalNanotezPerByte + 1;

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
            BbUri                   = configuration["BbUri"];
            BbApiUri                = configuration["BbApiUri"];

            BlockchainApi           = ResolveBlockchainApi(configuration, this);
            TxExplorerUri           = configuration["TxExplorerUri"];
            AddressExplorerUri      = configuration["AddressExplorerUri"];
            SwapContractAddress     = configuration["SwapContract"];
            TransactionType         = typeof(TezosTransaction);

            IsSwapAvailable         = true;
            Bip44Code               = Bip44.Tezos;

            BcdApi     = configuration["BcdApi"];
            BcdNetwork = configuration["BcdNetwork"];

            BcdSizeLimit = !string.IsNullOrEmpty(configuration["BcdSizeLimit"])
                ? int.Parse(configuration["BcdSizeLimit"])
                : 10;
        }

        protected static IBlockchainApi ResolveBlockchainApi(
            IConfiguration configuration,
            TezosConfig tezos)
        {
            var blockchainApi = configuration["BlockchainApi"]
                .ToLowerInvariant();

            return blockchainApi switch
            {
                "tzkt" => new TzktApi(tezos),
                _ => throw new NotSupportedException($"BlockchainApi {blockchainApi} not supported")
            };
        }

        public override IExtKey CreateExtKey(SecureBytes seed) =>
            new TezosExtKey(seed);

        public override IKey CreateKey(SecureBytes seed) =>
            new TezosKey(seed);

        public override string AddressFromKey(byte[] publicKey) =>
            Base58Check.Encode(
                payload: HmacBlake2b.Compute(publicKey, PkHashSize),
                prefix: Prefix.Tz1);

        public override bool IsValidAddress(string address) =>
            Address.CheckTz1Address(address) ||
            Address.CheckTz2Address(address) ||
            Address.CheckTz3Address(address) ||
            Address.CheckKtAddress(address);

        public override bool IsAddressFromKey(string address, byte[] publicKey) =>
             AddressFromKey(publicKey).ToLowerInvariant()
                .Equals(address.ToLowerInvariant());

        public override bool VerifyMessage(byte[] data, byte[] signature, byte[] publicKey) =>
            TezosSigner.Verify(
                data: data,
                signature: signature,
                publicKey: publicKey);
 
        public override decimal GetFeeAmount(decimal fee, decimal feePrice) =>
            fee;

        public override decimal GetFeeFromFeeAmount(decimal feeAmount, decimal feePrice) =>
            feeAmount;

        public override decimal GetFeePriceFromFeeAmount(decimal feeAmount, decimal fee) =>
            1m;

        public override Task<decimal> GetPaymentFeeAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InitiateFee.ToTez());

        public override Task<decimal> GetRedeemFeeAsync(
            WalletAddress toAddress = null,
            CancellationToken cancellationToken = default)
        {
            var result = (RedeemFee + RevealFee + MicroTezReserve + //todo: define another value for revealed
                (toAddress != null && toAddress.AvailableBalance() > 0
                    ? 0
                    : ActivationStorage * StorageFeeMultiplier))
                .ToTez();

            return Task.FromResult(result);
        }

        public override Task<decimal> GetEstimatedRedeemFeeAsync(
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

        public static decimal MtzToTz(decimal mtz) =>
            mtz / XtzDigitsMultiplier;

        public override decimal GetMaximumFee() =>
            MaxFee.ToTez();

        private static bool CheckAddress(string address, byte[] prefix)
        {
            try
            {
                Base58Check.Decode(address, prefix);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckAddress(string address) =>
            CheckAddress(address, Prefix.Tz1) ||
            CheckAddress(address, Prefix.Tz2) ||
            CheckAddress(address, Prefix.Tz3) ||
            CheckAddress(address, Prefix.KT);

        public static string ParseAddress(JToken michelineExpr)
        {
            if (michelineExpr["string"] != null)
            {
                var address = michelineExpr["string"].Value<string>();
                if (!CheckAddress(address))
                    throw new FormatException($"Invalid address: {address}");

                return address;
            }

            if (michelineExpr["bytes"] != null)
            {
                var hex = michelineExpr["bytes"].Value<string>();
                var raw = Hex.FromString(hex);

                if (raw.Length != 22)
                    throw new ArgumentException($"Invalid address size: {raw.Length}");

                var data = hex.Substring(0, 4) switch
                {
                    "0000" => Prefix.Tz1.ConcatArrays(raw.SubArray(2)),
                    "0001" => Prefix.Tz2.ConcatArrays(raw.SubArray(2)),
                    "0002" => Prefix.Tz3.ConcatArrays(raw.SubArray(2)),
                    _ => raw[0] == 0x01 && raw[21] == 0x00 ? Prefix.KT.ConcatArrays(raw.SubArray(1, 20)) : null
                };
                if (data == null)
                    throw new ArgumentException($"Unknown address prefix: {hex}");

                return Base58Check.Encode(data);
            }

            throw new ArgumentException($"Either string or bytes are accepted: {michelineExpr}");
        }

        public static long ParseTimestamp(JToken michelineExpr)
        {
            if (michelineExpr["int"] != null)
            {
                var timestamp = michelineExpr["int"].Value<long>();
                if (timestamp < 0)
                    throw new ArgumentException("Timestamp cannot be negative");

                return timestamp;
            }

            if (michelineExpr["string"] != null)
                return michelineExpr["string"].Value<DateTime>().ToUnixTimeSeconds();

            throw new ArgumentException($"Either int or string are accepted: {michelineExpr}");
        }

        public BcdApiSettings BcdApiSettings => new BcdApiSettings
        {
            Uri     = BcdApi,
            Network = BcdNetwork,
            MaxSize = BcdSizeLimit
        };
    }
}