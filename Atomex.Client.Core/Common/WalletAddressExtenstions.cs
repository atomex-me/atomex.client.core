using System;
using System.Collections.Generic;
using System.Linq;
using Atomex.Core.Entities;

namespace Atomex.Common
{
    public static class WalletAddressExtenstions
    {
        public static WalletAddress ResolveCurrencyById(
            this WalletAddress walletAddress,
            IList<Currency> currencies)
        {
            if (walletAddress == null)
                return walletAddress;

            walletAddress.Currency = currencies.FirstOrDefault(c => c.Id == walletAddress.CurrencyId);

            if (walletAddress.Currency == null)
                throw new Exception("Currency resolving error");

            return walletAddress;
        }

        public static WalletAddress ResolveCurrencyByName(
            this WalletAddress walletAddress,
            IList<Currency> currencies)
        {
            if (walletAddress == null)
                return walletAddress;

            walletAddress.Currency = currencies.FirstOrDefault(c => c.Name == walletAddress.Currency?.Name);

            if (walletAddress.Currency == null)
                throw new Exception("Currency resolving error");

            return walletAddress;
        }
    }
}