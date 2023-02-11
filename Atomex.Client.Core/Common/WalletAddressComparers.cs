using System.Collections.Generic;

using Atomex.Core;
using Atomex.ViewModels;

namespace Atomex.Common
{
    public class AvailableBalanceAscending : IComparer<WalletAddress>
    {
        public AvailableBalanceAscending()
        {
        }

        public int Compare(WalletAddress x, WalletAddress y) =>
            x.AvailableBalance().CompareTo(y.AvailableBalance());
    }

    public class AvailableBalanceDescending : IComparer<WalletAddress>
    {
        public AvailableBalanceDescending()
        {
        }

        public int Compare(WalletAddress x, WalletAddress y) =>
            y.AvailableBalance().CompareTo(x.AvailableBalance());
    }

    public class KeyPathAscending<T> : IComparer<T> where T : IWalletAddressViewModel
    {
        public KeyPathAscending() { }

        public int Compare(T x, T y)
        {
            var type = x.WalletAddress.KeyType.CompareTo(y.WalletAddress.KeyType);

            if (type != 0)
                return type;

            return x.WalletAddress.KeyPath.CompareTo(y.WalletAddress.KeyPath);
        }
    }

    public class KeyPathDescending<T> : IComparer<T> where T : IWalletAddressViewModel
    {
        public KeyPathDescending() { }

        public int Compare(T x, T y)
        {
            var type = y.WalletAddress.KeyType.CompareTo(x.WalletAddress.KeyType);

            if (type != 0)
                return type;

            return y.WalletAddress.KeyPath.CompareTo(x.WalletAddress.KeyPath);
        }
    }
}