using System;
using System.Collections.Generic;

using Atomex.Blockchain.Bitcoin;
using Atomex.Client.V1.Entities;
using Atomex.Common;

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
        public string RedeemScript { get; set; }
        public string RefundAddress { get; set; }
        public string PartyAddress { get; set; }
        public decimal PartyRewardForRedeem { get; set; }
        public string PartyRedeemScript { get; set; }
        public string PartyRefundAddress { get; set; }
        public decimal MakerNetworkFee { get; set; }

        public string FromAddress { get; set; }
        public List<BitcoinTxOutput> FromOutputs { get; set; }
        public string RedeemFromAddress { get; set; }

        public string SoldCurrency => Symbol.SoldCurrency(Side);
        public string PurchasedCurrency => Symbol.PurchasedCurrency(Side);

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
            set
            {
                _secret = value;
                StateFlags |= SwapStateFlags.HasSecret;
            }
        }

        private byte[] _secretHash;
        public byte[] SecretHash
        {
            get => _secretHash;
            set
            {
                _secretHash = value;
                StateFlags |= SwapStateFlags.HasSecretHash;
            }
        }

        private string _paymentTxId;
        public string PaymentTxId
        {
            get => _paymentTxId;
            set
            {
                _paymentTxId = value;
                StateFlags |= SwapStateFlags.HasPayment;
            }
        }

        public string _redeemTxId;
        public string RedeemTxId
        {
            get => _redeemTxId;
            set
            {
                _redeemTxId = value;
                StateFlags |= SwapStateFlags.HasRedeem;
            }
        }

        public string _refundTxId;
        public string RefundTxId
        {
            get => _refundTxId;
            set
            {
                _refundTxId = value;
                StateFlags |= SwapStateFlags.HasRefund;
            }
        }

        public string _partyPaymentTxId;
        public string PartyPaymentTxId
        {
            get => _partyPaymentTxId;
            set
            {
                _partyPaymentTxId = value;
                StateFlags |= SwapStateFlags.HasPartyPayment;
            }
        }

        public DateTime LastRedeemTryTimeStamp { get; set; }
        public DateTime LastRefundTryTimeStamp { get; set; }
    }
}