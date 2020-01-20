using System.Collections.Generic;
using Atomex.Core;

namespace Atomex.Common
{
    public class AvailableBalanceAscending : IComparer<WalletAddress>
    {
        public AvailableBalanceAscending()
        {
        }

        public int Compare(WalletAddress x, WalletAddress y)
        {
            return x.AvailableBalance().CompareTo(y.AvailableBalance());
        }
    }

    public class AvailableBalanceDescending : IComparer<WalletAddress>
    {
        public AvailableBalanceDescending()
        {
        }

        public int Compare(WalletAddress x, WalletAddress y)
        {
            return y.AvailableBalance().CompareTo(x.AvailableBalance());
        }
    }
}