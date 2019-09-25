using Atomex.Core.Entities;

namespace Atomex.Wallet.Abstract
{
    public class SelectedWalletAddress
    {
        public WalletAddress WalletAddress { get; set; }
        public decimal UsedAmount { get; set; }
        public decimal UsedFee { get; set; }
    }
}