using System;
using System.Globalization;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography;
using Atomex.Wallet.Bip;
using Atomex.Wallet.Tezos;
using Microsoft.Extensions.Configuration;

namespace Atomex
{
    public class Tezos : Currency
    {
        private const long XtzDigitsMultiplier = 1_000_000;
        private const int PkHashSize = 20 * 8;

        public decimal MinimalFee { get; private set; }
        public decimal MinimalNanotezPerGasUnit { get; private set; }
        public decimal MinimalNanotezPerByte { get; private set; }

        public decimal HeadSizeInBytes { get; private set; }
        public decimal SigSizeInBytes { get; private set; }

        public decimal MicroTezReserve { get; private set; }
        public decimal GasReserve { get; private set; }

        public decimal Fee { get; private set; }
        public decimal MaxFee { get; private set; }
        public decimal GasLimit { get; private set; }
        public decimal StorageLimit { get; private set; }

        public decimal RevealFee { get; private set; }
        public decimal RevealGasLimit { get; private set; }

        public decimal InitiateFee { get; private set; }
        public decimal InitiateGasLimit { get; private set; }
        public decimal InitiateStorageLimit { get; private set; }
        public decimal InitiateSize { get; private set; }

        public decimal AddFee { get; private set; }
        public decimal AddGasLimit { get; private set; }
        public decimal AddStorageLimit { get; private set; }
        public decimal AddSize { get; private set; }

        public decimal RedeemFee { get; private set; }
        public decimal RedeemGasLimit { get; private set; }
        public decimal RedeemStorageLimit { get; private set; }
        public decimal RedeemSize { get; private set; }

        public decimal RefundFee { get; private set; }
        public decimal RefundGasLimit { get; private set; }
        public decimal RefundStorageLimit { get; private set; }
        public decimal RefundSize { get; private set; }

        public decimal ActivationStorage { get; private set; }
        public decimal StorageFeeMultiplier { get; private set; }

        public string BaseUri { get; private set; }
        public string RpcNodeUri { get; private set; }
        public string BbApiUri { get; private set; }
        public string SwapContractAddress { get; private set; }

        public Tezos()
        {
        }

        public Tezos(IConfiguration configuration)
        {
            Update(configuration);
        }

        public void Update(IConfiguration configuration)
        {
            Name = configuration["Name"];
            Description = configuration["Description"];
            DigitsMultiplier = XtzDigitsMultiplier;
            Digits = (int)Math.Log10(XtzDigitsMultiplier);
            Format = $"F{Digits}";
            FeeDigits = Digits;
            FeeCode = Name;
            FeeFormat = $"F{FeeDigits}";
            HasFeePrice = false;

            MinimalFee               = decimal.Parse(configuration[nameof(MinimalFee)], CultureInfo.InvariantCulture);
            MinimalNanotezPerGasUnit = decimal.Parse(configuration[nameof(MinimalNanotezPerGasUnit)], CultureInfo.InvariantCulture);
            MinimalNanotezPerByte    = decimal.Parse(configuration[nameof(MinimalNanotezPerByte)], CultureInfo.InvariantCulture);

            HeadSizeInBytes = decimal.Parse(configuration[nameof(HeadSizeInBytes)], CultureInfo.InvariantCulture);
            SigSizeInBytes  = decimal.Parse(configuration[nameof(SigSizeInBytes)], CultureInfo.InvariantCulture);

            MicroTezReserve = decimal.Parse(configuration[nameof(MicroTezReserve)], CultureInfo.InvariantCulture);
            GasReserve = decimal.Parse(configuration[nameof(GasReserve)], CultureInfo.InvariantCulture);

            Fee          = decimal.Parse(configuration[nameof(Fee)], CultureInfo.InvariantCulture);
            MaxFee       = decimal.Parse(configuration[nameof(MaxFee)], CultureInfo.InvariantCulture);

            GasLimit     = decimal.Parse(configuration[nameof(GasLimit)], CultureInfo.InvariantCulture);
            StorageLimit = decimal.Parse(configuration[nameof(StorageLimit)], CultureInfo.InvariantCulture);

            RevealFee = decimal.Parse(configuration[nameof(RevealFee)], CultureInfo.InvariantCulture);
            RevealGasLimit = decimal.Parse(configuration[nameof(RevealGasLimit)], CultureInfo.InvariantCulture);

            InitiateGasLimit     = decimal.Parse(configuration[nameof(InitiateGasLimit)], CultureInfo.InvariantCulture);
            InitiateStorageLimit = decimal.Parse(configuration[nameof(InitiateStorageLimit)], CultureInfo.InvariantCulture);
            InitiateSize         = decimal.Parse(configuration[nameof(InitiateSize)], CultureInfo.InvariantCulture);
            InitiateFee          = MinimalFee + (InitiateGasLimit + GasReserve) * MinimalNanotezPerGasUnit + InitiateSize * MinimalNanotezPerByte + 1;

            AddGasLimit     = decimal.Parse(configuration[nameof(AddGasLimit)], CultureInfo.InvariantCulture);
            AddStorageLimit = decimal.Parse(configuration[nameof(AddStorageLimit)], CultureInfo.InvariantCulture);
            AddSize         = decimal.Parse(configuration[nameof(AddSize)], CultureInfo.InvariantCulture);
            AddFee          = MinimalFee + (AddGasLimit + GasReserve) * MinimalNanotezPerGasUnit + AddSize * MinimalNanotezPerByte + 1;

            RedeemGasLimit     = decimal.Parse(configuration[nameof(RedeemGasLimit)], CultureInfo.InvariantCulture);
            RedeemStorageLimit = decimal.Parse(configuration[nameof(RedeemStorageLimit)], CultureInfo.InvariantCulture);
            RedeemSize         = decimal.Parse(configuration[nameof(RedeemSize)], CultureInfo.InvariantCulture);
            RedeemFee          = MinimalFee + (RedeemGasLimit + GasReserve) * MinimalNanotezPerGasUnit + RedeemSize * MinimalNanotezPerByte + 1;

            RefundGasLimit     = decimal.Parse(configuration[nameof(RefundGasLimit)], CultureInfo.InvariantCulture);
            RefundStorageLimit = decimal.Parse(configuration[nameof(RefundStorageLimit)], CultureInfo.InvariantCulture);
            RefundSize         = decimal.Parse(configuration[nameof(RefundSize)], CultureInfo.InvariantCulture);
            RefundFee          = MinimalFee + (RefundGasLimit + GasReserve) * MinimalNanotezPerGasUnit + RefundStorageLimit * MinimalNanotezPerByte + 1;

            ActivationStorage    = decimal.Parse(configuration[nameof(ActivationStorage)], CultureInfo.InvariantCulture);
            StorageFeeMultiplier = decimal.Parse(configuration[nameof(StorageFeeMultiplier)], CultureInfo.InvariantCulture);

            BaseUri    = configuration["BlockchainApiBaseUri"];
            RpcNodeUri = configuration["BlockchainRpcNodeUri"];
            BbApiUri = configuration["BbApiUri"];

            BlockchainApi       = ResolveBlockchainApi(configuration, this);
            TxExplorerUri       = configuration["TxExplorerUri"];
            AddressExplorerUri  = configuration["AddressExplorerUri"];
            SwapContractAddress = configuration["SwapContract"];
            TransactionType     = typeof(TezosTransaction);

            IsTransactionsAvailable = true;
            IsSwapAvailable = true;
            Bip44Code = Bip44.Tezos;
        }

        private static IBlockchainApi ResolveBlockchainApi(
            IConfiguration configuration,
            Tezos tezos)
        {
            var blockchainApi = configuration["BlockchainApi"]
                .ToLowerInvariant();

            if (blockchainApi.Equals("tzkt"))
                return new BabyTzktApi(tezos);

            throw new NotSupportedException($"BlockchainApi {blockchainApi} not supported");
        }

        public override IExtKey CreateExtKey(SecureBytes seed)
        {
            //return new TrustWalletTezosExtKey(seed);
            return new TezosExtKey(seed);
        }

        public override IKey CreateKey(SecureBytes seed)
        {
            return new TezosKey(seed);
        }

        public override string AddressFromKey(byte[] publicKey)
        {
            return Base58Check.Encode(
                payload: new HmacBlake2b(PkHashSize).ComputeHash(publicKey),
                prefix: Prefix.Tz1);
        }

        public override bool IsValidAddress(string address)
        {
            return Address.CheckTz1Address(address) ||
                   Address.CheckTz2Address(address) ||
                   Address.CheckTz3Address(address) ||
                   Address.CheckKtAddress(address);
        }

        public override bool IsAddressFromKey(string address, byte[] publicKey)
        {
            return AddressFromKey(publicKey).ToLowerInvariant()
                .Equals(address.ToLowerInvariant());
        }

        public override bool VerifyMessage(byte[] data, byte[] signature, byte[] publicKey)
        {
            return TezosSigner.Verify(
                data: data,
                signature: signature,
                publicKey: publicKey);
        }

        public override decimal GetFeeAmount(decimal fee, decimal feePrice)
        {
            return fee;
        }

        public override decimal GetFeeFromFeeAmount(decimal feeAmount, decimal feePrice)
        {
            return feeAmount;
        }

        public override decimal GetFeePriceFromFeeAmount(decimal feeAmount, decimal fee)
        {
            return 1m;
        }

        public override decimal GetDefaultRedeemFee(WalletAddress toAddress = null)
        {
            return RedeemFee.ToTez() + RevealFee.ToTez() + MicroTezReserve.ToTez() +  //todo: define another value for revealed
                (toAddress.AvailableBalance() > 0
                    ? 0
                    : ActivationStorage / StorageFeeMultiplier);
        }

        public static decimal MtzToTz(decimal mtz)
        {
            return mtz / XtzDigitsMultiplier;
        }

        public override decimal GetMaximumFee()
        {
            return MaxFee.ToTez();
        }
    }
}
