using System.Numerics;

using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public class SelectedWalletAddress
    {
        public WalletAddress WalletAddress { get; set; }
        public BigInteger UsedAmount { get; set; }
        public BigInteger UsedFee { get; set; }
        public BigInteger UsedStorageFee { get; set; }
    }
}