using System;
using Atomix.Core.Entities;

namespace Atomix.Swaps
{
    public class SwapRequisites
    {
        public WalletAddress ToWallet { get; set; }
        public WalletAddress RefundWallet { get; set; }

        public SwapRequisites()
        {
        }

        public SwapRequisites(WalletAddress toWallet, WalletAddress refundWallet)
        {
            ToWallet = toWallet ?? throw new ArgumentNullException(nameof(toWallet));
            RefundWallet = refundWallet ?? throw new ArgumentNullException(nameof(refundWallet));
        }
    }
}