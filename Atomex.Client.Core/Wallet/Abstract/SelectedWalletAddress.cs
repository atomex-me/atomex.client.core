using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public class SelectedWalletAddress
    {
        public WalletAddress_OLD WalletAddress { get; set; }
        public decimal UsedAmount { get; set; }
        public decimal UsedFee { get; set; }
        public decimal UsedStorageFee { get; set; }

    }
}