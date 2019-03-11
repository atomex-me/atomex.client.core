using System;
using Atomix.Core.Entities;

namespace Atomix.Swaps
{
    public enum SwapDataType
    {
        SecretHash,
        InitiatorPayment,
        InitiatorRefund,
        InitiatorRefundSigned,
        InitiatorPaymentTxId,
        //InitiatorRedeem,
        CounterPartyPayment,
        CounterPartyRefund,
        CounterPartyRefundSigned,
        CounterPartyPaymentTxId,
        //CounterPartyRedeem
        Canceled,
        LockTimeWarning
    }

    public class SwapData
    {
        public Guid SwapId { get; set; }
        public string UserId { get; set; }
        public Symbol Symbol { get; set; }
        public SwapDataType Type { get; set; }
        public byte[] Data { get; set; }
    }
}