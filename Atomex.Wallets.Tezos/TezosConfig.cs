using System;

using Atomex.Blockchain.Tezos;
using Atomex.Common.Memory;
using Atomex.Cryptography;
using Atomex.Cryptography.Abstract;
using Atomex.Wallets.Abstract;
using Atomex.Wallets.Tezos.Common;

namespace Atomex.Wallets.Tezos
{
    public class TezosConfig : CurrencyConfig
    {
        public const int HeadSizeInBytes = 32;
        public const int SignatureSizeInBytes = 64;

        public TezosApiSettings ApiSettings { get; set; }

        public string ChainId { get; set; } = "NetXdQprcVkpaWU";
        public int RevealGasLimit { get; set; } = 1200;
        public int ReserveGasLimit { get; set; } = 100;
        public int ActivationStorageLimit { get; set; } = 257;

        public int MinimalFee { get; set; } = 100;
        public decimal MinimalNanotezPerGasUnit { get; set; } = 0.1m;
        public decimal MinimalNanotezPerByte { get; set; } = 1;

        public override string AddressFromKey(
            SecureBytes publicKey,
            WalletInfo walletInfo = null)
        {
            var prefix = walletInfo.AdditionalType switch
            {
                TezosWalletType.Tz1 => TezosPrefixes.Tz1,
                TezosWalletType.Tz2 => TezosPrefixes.Tz2,
                TezosWalletType.Tz3 => TezosPrefixes.Tz3,
                _ => throw new NotSupportedException($"Tezos wallet type {walletInfo.AdditionalType} not supported")
            };

            using var pubKey = publicKey.ToUnmanagedBytes();

            return Base58Check.Encode(
                data: HashAlgorithm.Blake2b160.Hash(pubKey),
                prefix: prefix);
        }
    }
}