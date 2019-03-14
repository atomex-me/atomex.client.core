using System;

namespace Atomix.Swaps.Abstract
{
    [Flags]
    public enum SwapStateFlags
    {
        Empty                   = 0,
        HasSecret               = 1,
        HasSecretHash           = 1 << 1,

        HasPayment              = 1 << 2,
        IsPaymentSigned         = 1 << 3,
        IsPaymentBroadcast      = 1 << 4,
        IsPaymentConfirmed      = 1 << 5,
        IsPaymentSpent          = 1 << 6,

        HasRefund               = 1 << 7,
        IsRefundSigned          = 1 << 8,
        IsRefundBroadcast       = 1 << 9,
        IsRefundConfirmed       = 1 << 10,

        HasRedeem               = 1 << 11,
        IsRedeemSigned          = 1 << 12,
        IsRedeemBroadcast       = 1 << 13,
        IsRedeemConfirmed       = 1 << 14,
        IsRedeemSpent           = 1 << 15,

        HasPartyPayment         = 1 << 16,
        IsPartyPaymentSigned    = 1 << 17,
        IsPartyPaymentBroadcast = 1 << 18,
        IsPartyPaymentConfirmed = 1 << 19,
        IsPartyPaymentSpent     = 1 << 20,

        HasPartyRefund          = 1 << 21,
        IsPartyRefundSigned     = 1 << 22,
        //IsPartyRefundBroadcast  = 1 << 23,
        //IsPartyRefundConfirmed  = 1 << 24,

        HasPartyRedeem          = 1 << 25,
        //IsPartyRedeemSigned    = 1 << 26,
        //IsPartyRedeemBroadcast = 1 << 27,
        //IsPartyRedeemConfirmed = 1 << 28,
        IsPartyRedeemSpent      = 1 << 29,

        IsCanceled              = 1 << 30,
    }

    public interface ISwapState
    {
        Guid Id { get; }
        SwapStateFlags StateFlags { get; }

        byte[] Secret { get; set; }
        byte[] SecretHash { get; }
    }
}