using System;
using System.Globalization;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Core.Entities;
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

        public decimal Fee { get; private set; }
        public decimal GasLimit { get; private set; }
        public decimal StorageLimit { get; private set; }

        public decimal InitiateFee { get; private set; }
        public decimal InitiateGasLimit { get; private set; }
        public decimal InitiateStorageLimit { get; private set; }

        public decimal AddFee { get; private set; }
        public decimal AddGasLimit { get; private set; }
        public decimal AddStorageLimit { get; private set; }

        public decimal RedeemFee { get; private set; }
        public decimal RedeemGasLimit { get; private set; }
        public decimal RedeemStorageLimit { get; private set; }

        public decimal RefundFee { get; private set; }
        public decimal RefundGasLimit { get; private set; }
        public decimal RefundStorageLimit { get; private set; }

        public decimal ActivationFee { get; private set; }

        private TezosNetwork Network { get; set; }
        public string RpcProvider { get; private set; }
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

            Fee = decimal.Parse(configuration[nameof(Fee)], CultureInfo.InvariantCulture);
            GasLimit = decimal.Parse(configuration[nameof(GasLimit)], CultureInfo.InvariantCulture);
            StorageLimit = decimal.Parse(configuration[nameof(StorageLimit)], CultureInfo.InvariantCulture);

            InitiateFee = decimal.Parse(configuration[nameof(InitiateFee)], CultureInfo.InvariantCulture);
            InitiateGasLimit = decimal.Parse(configuration[nameof(InitiateGasLimit)], CultureInfo.InvariantCulture);
            InitiateStorageLimit = decimal.Parse(configuration[nameof(InitiateStorageLimit)], CultureInfo.InvariantCulture);

            AddFee = decimal.Parse(configuration[nameof(AddFee)], CultureInfo.InvariantCulture);
            AddGasLimit = decimal.Parse(configuration[nameof(AddGasLimit)], CultureInfo.InvariantCulture);
            AddStorageLimit = decimal.Parse(configuration[nameof(AddStorageLimit)], CultureInfo.InvariantCulture);

            RedeemFee = decimal.Parse(configuration[nameof(RedeemFee)], CultureInfo.InvariantCulture);
            RedeemGasLimit = decimal.Parse(configuration[nameof(RedeemGasLimit)], CultureInfo.InvariantCulture);
            RedeemStorageLimit = decimal.Parse(configuration[nameof(RedeemStorageLimit)], CultureInfo.InvariantCulture);

            RefundFee = decimal.Parse(configuration[nameof(RefundFee)], CultureInfo.InvariantCulture);
            RefundGasLimit = decimal.Parse(configuration[nameof(RefundGasLimit)], CultureInfo.InvariantCulture);
            RefundStorageLimit = decimal.Parse(configuration[nameof(RefundStorageLimit)], CultureInfo.InvariantCulture);

            ActivationFee = decimal.Parse(configuration[nameof(ActivationFee)], CultureInfo.InvariantCulture);

            Network = ResolveNetwork(configuration);
            RpcProvider = TzScanApi.RpcByNetwork(Network);
            BlockchainApi = new TzScanApi(this, Network);
            TxExplorerUri = configuration["TxExplorerUri"];
            AddressExplorerUri = configuration["AddressExplorerUri"];
            SwapContractAddress = configuration["SwapContract"];
            TransactionType = typeof(TezosTransaction);

            IsTransactionsAvailable = true;
            IsSwapAvailable = true;
            Bip44Code = Bip44.Tezos;
        }

        private static TezosNetwork ResolveNetwork(IConfiguration configuration)
        {
            var chain = configuration["Chain"]
                .ToLowerInvariant();

            if (chain.Equals("mainnet"))
                return TezosNetwork.Mainnet;

            if (chain.Equals("alphanet"))
                return TezosNetwork.Alphanet;

            throw new NotSupportedException($"Chain {chain} not supported");
        }

        public override IExtKey CreateExtKey(byte[] seed)
        {
            //return new TrustWalletTezosExtKey(seed);
            return new TezosExtKey(seed);
        }

        public override IKey CreateKey(byte[] seed)
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

        public override decimal GetDefaultRedeemFee()
        {
            return RedeemFee.ToTez();
        }

        public static decimal MtzToTz(decimal mtz)
        {
            return mtz / XtzDigitsMultiplier;
        }
    }
}
