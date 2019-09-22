using System;

namespace Atomex.Core.Entities
{
    [Flags]
    public enum SwapStatus
    {
        Empty = 0,
        Initiated = 0x01,
        Accepted = 0x02,
        InitiatorPaymentReceived = 0x04,
        AcceptorPaymentReceived = 0x08,
        InitiatorRedeemReceived = 0x10,
        AcceptorRedeemReceived = 0x20,
        InitiatorRefundReceived = 0x40,
        AcceptorRefundReceived = 0x80
    }

    public class Swap
    {
        public long Id { get; set; }
        public SwapStatus Status { get; set; }
        public string InitiatorUserId { get; set; }
        public long InitiatorOrderId { get; set; }
        public Side InitiatorOrderSide { get; set; }
        public string InitiatorAddress { get; set; }
        public decimal InitiatorRewardForRedeem { get; set; }
        public string InitiatorPaymentTxId { get; set; }
        public string InitiatorRedeemScript { get; set; }
        public string AcceptorUserId { get; set; }
        public long AcceptorOrderId { get; set; }
        public Side AcceptorOrderSide { get; set; }
        public string AcceptorAddress { get; set; }
        public decimal AcceptorRewardForRedeem { get; set; }
        public string AcceptorPaymentTxId { get; set; }
        public string AcceptorRedeemScript { get; set; }
        public byte[] SecretHash { get; set; }
        public DateTime TimeStamp { get; set; }
        public int SymbolId { get; set; }
        public Symbol Symbol { get; set; }
        public decimal Price { get; set; }
        public decimal Qty { get; set; }

        public ClientSwap GetInitiatorSwap()
        {
            return new ClientSwap
            {
                Id = Id,
                UserId = InitiatorUserId,
                Status = Status,
                SecretHash = SecretHash,
                TimeStamp = TimeStamp,
                OrderId = InitiatorOrderId,
                Symbol = Symbol,
                Side = InitiatorOrderSide,
                Price = Price,
                Qty = Qty,
                IsInitiative = true,
                ToAddress = InitiatorAddress,
                RewardForRedeem = InitiatorRewardForRedeem,
                PaymentTxId = InitiatorPaymentTxId,
                RedeemScript = InitiatorRedeemScript,
                PartyAddress = AcceptorAddress,
                PartyRewardForRedeem = AcceptorRewardForRedeem,
                PartyPaymentTxId = AcceptorPaymentTxId,
                PartyRedeemScript = AcceptorRedeemScript
            };
        }

        public ClientSwap GetAcceptorSwap()
        {
            return new ClientSwap
            {
                Id = Id,
                UserId = AcceptorUserId,
                Status = Status,
                SecretHash = SecretHash,
                TimeStamp = TimeStamp,
                OrderId = AcceptorOrderId,
                Symbol = Symbol,
                Side = AcceptorOrderSide,
                Price = Price,
                Qty = Qty,
                IsInitiative = false,
                ToAddress = AcceptorAddress,
                RewardForRedeem = AcceptorRewardForRedeem,
                PaymentTxId = AcceptorPaymentTxId,
                RedeemScript = AcceptorRedeemScript,
                PartyAddress = InitiatorAddress,
                PartyRewardForRedeem = InitiatorRewardForRedeem,
                PartyPaymentTxId = InitiatorPaymentTxId,
                PartyRedeemScript = InitiatorRedeemScript
            };
        }
    }
}