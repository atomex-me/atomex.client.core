using System.Collections.Generic;

using Atomex.Abstract;
using Atomex.Core;
using Atomex.Wallet.Abstract;

namespace Atomex.Common
{
    public static class WalletAddressExtensions
    {
        public static WalletAddress ResolvePublicKey(
            this WalletAddress address,
            ICurrencies currencies,
            IHdWallet wallet)
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

        public static IList<WalletAddress> ResolvePublicKeys(
            this IList<WalletAddress> addresses,
            ICurrencies currencies,
            IHdWallet wallet)
        {
            foreach (var address in addresses)
                ResolvePublicKey(address, currencies, wallet);

            return addresses;
        }
    }
}