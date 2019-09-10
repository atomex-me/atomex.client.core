using System;
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

        public decimal Fee { get; }
        public decimal GasLimit { get; }
        public decimal StorageLimit { get; }

        public decimal InitiateFee { get; }
        public decimal InitiateGasLimit { get; }
        public decimal InitiateStorageLimit { get; }

        public decimal AddFee { get; }
        public decimal AddGasLimit { get; }
        public decimal AddStorageLimit { get; }

        public decimal RedeemFee { get; }
        public decimal RedeemGasLimit { get; }
        public decimal RedeemStorageLimit { get; }

        public decimal RefundFee { get; }
        public decimal RefundGasLimit { get; }
        public decimal RefundStorageLimit { get; }

        public decimal ActivationFee { get; }

        private TezosNetwork Network { get; }
        public string RpcProvider { get; }
        public string SwapContractAddress { get; }

        public Tezos()
        {
        }

        public Tezos(IConfiguration configuration)
        {
            Name             = configuration["Name"];
            Description      = configuration["Description"];
            DigitsMultiplier = XtzDigitsMultiplier;
            Digits           = (int)Math.Log10(XtzDigitsMultiplier);
            Format           = $"F{Digits}";
            FeeDigits        = Digits;
            FeeCode          = Name;
            FeeFormat        = $"F{FeeDigits}";
            HasFeePrice      = false;

            Fee          = decimal.Parse(configuration[nameof(Fee)]);
            GasLimit     = decimal.Parse(configuration[nameof(GasLimit)]);
            StorageLimit = decimal.Parse(configuration[nameof(StorageLimit)]);

            InitiateFee          = decimal.Parse(configuration[nameof(InitiateFee)]);
            InitiateGasLimit     = decimal.Parse(configuration[nameof(InitiateGasLimit)]);
            InitiateStorageLimit = decimal.Parse(configuration[nameof(InitiateStorageLimit)]);

            AddFee          = decimal.Parse(configuration[nameof(AddFee)]);
            AddGasLimit     = decimal.Parse(configuration[nameof(AddGasLimit)]);
            AddStorageLimit = decimal.Parse(configuration[nameof(AddStorageLimit)]);

            RedeemFee           = decimal.Parse(configuration[nameof(RedeemFee)]);
            RedeemGasLimit      = decimal.Parse(configuration[nameof(RedeemGasLimit)]);
            RedeemStorageLimit  = decimal.Parse(configuration[nameof(RedeemStorageLimit)]);

            RefundFee           = decimal.Parse(configuration[nameof(RefundFee)]);
            RefundGasLimit      = decimal.Parse(configuration[nameof(RefundGasLimit)]);
            RefundStorageLimit  = decimal.Parse(configuration[nameof(RefundStorageLimit)]);

            ActivationFee = decimal.Parse(configuration[nameof(ActivationFee)]);

            Network             = ResolveNetwork(configuration);
            RpcProvider         = TzScanApi.RpcByNetwork(Network);
            BlockchainApi       = new TzScanApi(this, Network);
            TxExplorerUri       = configuration["TxExplorerUri"];
            AddressExplorerUri  = configuration["AddressExplorerUri"];
            SwapContractAddress = configuration["SwapContract"];
            TransactionType     = typeof(TezosTransaction);

            IsTransactionsAvailable = true;
            IsSwapAvailable         = true;
            Bip44Code               = Bip44.Tezos;
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

        public static decimal MtzToTz(
            decimal mtz)
        {
            return mtz / XtzDigitsMultiplier;
        }
    }
}
