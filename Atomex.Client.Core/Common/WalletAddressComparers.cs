using System.Collections.Generic;
using Atomex.Core;

namespace Atomex.Common
{
    public class AvailableBalanceAscending : IComparer<WalletAddress_OLD>
    {
        public AvailableBalanceAscending()
        {
        }

        public int Compare(WalletAddress_OLD x, WalletAddress_OLD y)
        {
            return x.AvailableBalance().CompareTo(y.AvailableBalance());
        }
    }

    public class AvailableBalanceDescending : IComparer<WalletAddress_OLD>
    {
        public AvailableBalanceDescending()
        {
        }

        public int Compare(WalletAddress_OLD x, WalletAddress_OLD y)
        {
            return y.AvailableBalance().CompareTo(x.AvailableBalance());
        }
    }
}