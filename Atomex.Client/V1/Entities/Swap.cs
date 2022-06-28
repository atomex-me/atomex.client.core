using System;

using Atomex.Common;

namespace Atomex.Client.V1.Entities
{
    [Flags]
    public enum SwapStatus
    {
        Empty     = 0,
        Initiated = 0x01,
        Accepted  = 0x02,
    }

    public class Swap
    {
        public long Id { get; set; }
        public SwapStatus Status { get; set; }
        public DateTime TimeStamp { get; set; }
        public long OrderId { get; set; }
        public string Symbol { get; set; }
        public Side Side { get; set; }
        public decimal Price { get; set; }
        public decimal Qty { get; set; }
        public bool IsInitiative { get; set; }
        public string ToAddress { get; set; }
        public decimal RewardForRedeem { get; set; }
        public string PaymentTxId { get; set; }
        public string RedeemScript { get; set; }
        public string RefundAddress { get; set; }
        public string PartyAddress { get; set; }
        public decimal PartyRewardForRedeem { get; set; }
        public string PartyPaymentTxId { get; set; }
        public string PartyRedeemScript { get; set; }
        public string PartyRefundAddress { get; set; }
        public byte[] SecretHash { get; set; }
    }
}