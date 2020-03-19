using Atomex.Core;

namespace Atomex.Common
{
    public static class SwapExtensions
    {
        public static void Cancel(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsCanceled;
        }

        public static void SetPaymentSigned(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsPaymentSigned;
        }

        public static void SetPaymentBroadcast(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;
        }

        public static void SetRefundSigned(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundSigned;
        }

        public static void SetRefundBroadcast(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;
        }

        public static void SetRedeemSigned(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemSigned;
        }

        public static void SetRedeemBroadcast(this Swap swap)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;
        }
    }
}