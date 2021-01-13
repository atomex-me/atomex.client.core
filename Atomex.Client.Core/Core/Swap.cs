﻿using System;

using Atomex.Blockchain.Abstract;

namespace Atomex.Core
{
    [Flags]
    public enum SwapStateFlags
    {
        Empty = 0,
        HasSecret = 1,
        HasSecretHash = 1 << 1,

        HasPayment = 1 << 2,
        IsPaymentSigned = 1 << 3,
        IsPaymentBroadcast = 1 << 4,
        IsPaymentConfirmed = 1 << 5,
        IsPaymentSpent = 1 << 6,

        HasRefund = 1 << 7,
        IsRefundSigned = 1 << 8,
        IsRefundBroadcast = 1 << 9,
        IsRefundConfirmed = 1 << 10,

        HasRedeem = 1 << 11,
        IsRedeemSigned = 1 << 12,
        IsRedeemBroadcast = 1 << 13,
        IsRedeemConfirmed = 1 << 14,

        HasPartyPayment = 1 << 15,
        IsPartyPaymentConfirmed = 1 << 16,

        IsCanceled = 1 << 17,
        IsUnsettled = 1 << 18
    }

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
        public string UserId { get; set; }
        public SwapStatus Status { get; set; }
        public SwapStateFlags StateFlags { get; set; }
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
        public decimal MakerMinerFee { get; set; }

        public string SoldCurrency =>
            Side == Side.Buy
                ? Symbol.Substring(Symbol.IndexOf('/') + 1)
                : Symbol.Substring(0, Symbol.IndexOf('/'));

        public string PurchasedCurrency =>
            Side == Side.Buy
                ? Symbol.Substring(0, Symbol.IndexOf('/'))
                : Symbol.Substring(Symbol.IndexOf('/') + 1);

        public bool IsSoldCurrency(string currency) => SoldCurrency == currency;
        public bool IsPurchasedCurrency(string currency) => PurchasedCurrency == currency;

        public bool IsComplete => StateFlags.HasFlag(SwapStateFlags.IsRedeemConfirmed);
        public bool IsRefunded => StateFlags.HasFlag(SwapStateFlags.IsRefundConfirmed);
        public bool IsCanceled => StateFlags.HasFlag(SwapStateFlags.IsCanceled);
        public bool IsUnsettled => StateFlags.HasFlag(SwapStateFlags.IsUnsettled);
        public bool IsActive => !IsComplete && !IsRefunded && !IsCanceled && !IsUnsettled;
        public bool IsInitiator => IsInitiative;
        public bool IsAcceptor => !IsInitiative;
        public bool HasPartyPayment => 
            StateFlags.HasFlag(SwapStateFlags.HasPartyPayment) &&
            StateFlags.HasFlag(SwapStateFlags.IsPartyPaymentConfirmed);

        private byte[] _secret;
        public byte[] Secret
        {
            get => _secret;
            set { _secret = value; StateFlags |= SwapStateFlags.HasSecret; }
        }

        private byte[] _secretHash;
        public byte[] SecretHash
        {
            get => _secretHash;
            set { _secretHash = value; StateFlags |= SwapStateFlags.HasSecretHash; }
        }

        private IBlockchainTransaction _paymentTx;
        public IBlockchainTransaction PaymentTx
        {
            get => _paymentTx;
            set { _paymentTx = value; if (_paymentTx != null) StateFlags |= SwapStateFlags.HasPayment; }
        }

        private IBlockchainTransaction _refundTx;
        public IBlockchainTransaction RefundTx
        {
            get => _refundTx;
            set { _refundTx = value; if (_refundTx != null) StateFlags |= SwapStateFlags.HasRefund; }
        }

        private IBlockchainTransaction _redeemTx;
        public IBlockchainTransaction RedeemTx
        {
            get => _redeemTx;
            set { _redeemTx = value; if (_redeemTx != null) StateFlags |= SwapStateFlags.HasRedeem; }
        }

        private IBlockchainTransaction _partyPaymentTx;
        public IBlockchainTransaction PartyPaymentTx
        {
            get => _partyPaymentTx;
            set { _partyPaymentTx = value; if (_partyPaymentTx != null) StateFlags |= SwapStateFlags.HasPartyPayment; }
        }

        public bool IsStatusSet(SwapStatus status, Enum flag) =>
            !Status.HasFlag(flag) && status.HasFlag(flag);

        public override string ToString() =>
            $"Id: {Id}, " +
            $"Status: {Status}, " +
            $"StateFlags: {StateFlags}, " +
            $"Side: {Side}, " +
            $"Price: {Price}, " +
            $"Qty: {Qty}, " +
            $"ToAddress: {ToAddress}, " +
            $"RewardForRedeem: {RewardForRedeem}, " +
            $"PaymentTxId: {PaymentTxId}, " +
            $"RedeemScript: {RedeemScript}, " +
            $"RefundAddress: {RefundAddress}, " +
            $"PartyAddress: {PartyAddress}, " +
            $"PartyRewardForRedeem: {PartyRewardForRedeem}, " +
            $"PartyPaymentTxId: {PartyPaymentTxId}, " +
            $"PartyRedeemScript: {PartyRedeemScript}, " +
            $"PartyRefundAddress: {PartyRefundAddress}.";
    }
}