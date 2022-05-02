using System.Collections.Generic;

using Atomex.Abstract;
using Atomex.Core;
using Atomex.Wallet.Abstract;

namespace Atomex.Common
{
    public static class WalletAddressExtensions
    {
        public static WalletAddress_OLD ResolvePublicKey(
            this WalletAddress_OLD address,
            ICurrencies currencies,
            IHdWallet_OLD wallet)
        {
            var currency = currencies.GetByName(address.Currency);

            address.PublicKey = wallet
                .GetAddress(
                    currency: currency,
                    keyIndex: address.KeyIndex,
                    keyType: address.KeyType)
                .PublicKey;

            return address;
        }

        public static IList<WalletAddress_OLD> ResolvePublicKeys(
            this IList<WalletAddress_OLD> addresses,
            ICurrencies currencies,
            IHdWallet_OLD wallet)
        {
            foreach (var address in addresses)
                ResolvePublicKey(address, currencies, wallet);

            return addresses;
        }
    }
}