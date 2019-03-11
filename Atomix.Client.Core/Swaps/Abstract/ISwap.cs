using System;

namespace Atomix.Swaps.Abstract
{
    [Flags]
    public enum SwapState
    {
        Empty                          = 0,
        HasSecret                      = 1,
        HasSecretHash                  = 1 << 1,
        HasInitiatorPayment            = 1 << 2,
        HasInitiatorPaymentSigned      = 1 << 3,
        IsInitiatorPaymentBroadcast    = 1 << 4,
        IsInitiatorPaymentConfirmed    = 1 << 5,
        IsInitiatorPaymentSpent        = 1 << 6,
        HasInitiatorRefund             = 1 << 7,
        HasInitiatorRefundSigned       = 1 << 8,
        IsInitiatorRefundBroadcast     = 1 << 9,
        IsInitiatorRefundConfirmed     = 1 << 10,
        HasInitiatorRedeem             = 1 << 11,
        HasInitiatorRedeemSigned       = 1 << 12,
        IsInitiatorRedeemBroadcast     = 1 << 13,
        IsInitiatorRedeemConfirmed     = 1 << 14,
        HasCounterPartyPayment         = 1 << 15,
        HasCounterPartyPaymentSigned   = 1 << 16,
        IsCounterPartyPaymentBroadcast = 1 << 17,
        IsCounterPartyPaymentConfirmed = 1 << 18,
        IsCounterPartyPaymentSpent     = 1 << 19,
        HasCounterPartyRefund          = 1 << 20,
        HasCounterPartyRefundSigned    = 1 << 21,
        IsCounterPartyRefundBroadcast  = 1 << 22,
        IsCounterPartyRefundConfirmed  = 1 << 23,
        HasCounterPartyRedeem          = 1 << 24,
        HasCounterPartyRedeemSigned    = 1 << 25,
        IsCounterPartyRedeemBroadcast  = 1 << 26,
        IsCounterPartyRedeemConfirmed  = 1 << 27,
        IsCanceled                     = 1 << 28,
    }

    public interface ISwap
    {
        Guid Id { get; }
        SwapState State { get; }
        byte[] SecretHash { get; }
    }
}