using System.Collections.Generic;

using Atomex.ViewModels;

namespace Atomex.Common
{
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