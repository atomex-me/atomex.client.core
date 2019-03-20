using System;
using Atomix.Blockchain.Abstract;
using Atomix.Core.Entities;
using Atomix.Swaps.Abstract;

namespace Atomix.Swaps
{
    public class SwapState : ISwapState
    {
        public event EventHandler<SwapEventArgs> Updated; 

        public Order Order { get; set; }
        public Guid Id => Order.SwapId;
        public SwapRequisites Requisites { get; set; }
        public SwapStateFlags StateFlags { get; set; }

        public bool IsInitiator => Order.SwapInitiative;
        public bool IsCounterParty => !Order.SwapInitiative;

        public bool IsComplete => StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast);
        public bool IsRefunded => StateFlags.HasFlag(SwapStateFlags.IsRefundConfirmed);
        public bool IsCanceled => StateFlags.HasFlag(SwapStateFlags.IsCanceled);

        private byte[] _secret;
        public byte[] Secret
        {
            get => _secret;
            set
            {
                _secret = value;
                StateFlags |= SwapStateFlags.HasSecret;
                RaiseUpdated();
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
                RaiseUpdated();
            }
        }

        private string _paymentTxId;
        public string PaymentTxId
        {
            get => _paymentTxId;
            set
            {
                _paymentTxId = value;
                RaiseUpdated();
            }
        }

        private string _partyPaymentTxId;
        public string PartyPaymentTxId
        {
            get => _partyPaymentTxId;
            set
            {
                _partyPaymentTxId = value;
                RaiseUpdated();
            }
        }

        private IBlockchainTransaction _paymentTx;
        public IBlockchainTransaction PaymentTx
        {
            get => _paymentTx;
            set
            {
                _paymentTx = value;
                StateFlags |= SwapStateFlags.HasPayment;
                RaiseUpdated();
            }
        }

        private IBlockchainTransaction _refundTx;
        public IBlockchainTransaction RefundTx
        {
            get => _refundTx;
            set
            {
                _refundTx = value;
                StateFlags |= SwapStateFlags.HasRefund;
                RaiseUpdated();
            }
        }

        private IBlockchainTransaction _redeemTx;
        public IBlockchainTransaction RedeemTx
        {
            get => _redeemTx;
            set
            {
                _redeemTx = value;
                StateFlags |= SwapStateFlags.HasRedeem;
                RaiseUpdated();
            }
        }

        private IBlockchainTransaction _partyPaymentTx;
        public IBlockchainTransaction PartyPaymentTx
        {
            get => _partyPaymentTx;
            set {
                _partyPaymentTx = value;
                StateFlags |= SwapStateFlags.HasPartyPayment;
                RaiseUpdated();
            }
        }

        private IBlockchainTransaction _partyRefundTx;
        public IBlockchainTransaction PartyRefundTx
        {
            get => _partyRefundTx;
            set {
                _partyRefundTx = value;
                StateFlags |= SwapStateFlags.HasPartyRefund;
                RaiseUpdated();
            }
        }

        private IBlockchainTransaction _partyRedeemTx;
        public IBlockchainTransaction PartyRedeemTx
        {
            get => _partyRedeemTx;
            set {
                _partyRedeemTx = value;
                StateFlags |= SwapStateFlags.HasPartyRedeem;
                RaiseUpdated();
            }
        }

        public SwapState() { }

        public SwapState(Order order, SwapRequisites requisites)
        {
            Order = order ?? throw new ArgumentNullException(nameof(order));
            Requisites = requisites ?? throw new ArgumentNullException(nameof(requisites));
        }

        protected void RaiseUpdated()
        {
            Updated?.Invoke(this, new SwapEventArgs(this));
        }

        public void Cancel()
        {
            StateFlags |= SwapStateFlags.IsCanceled;
            RaiseUpdated();
        }

        public void SetPaymentSigned()
        {
            StateFlags |= SwapStateFlags.IsPaymentSigned;
            RaiseUpdated();
        }

        public void SetPaymentBroadcast()
        {
            if (PaymentTx != null)
                PaymentTx.BlockInfo.FirstSeen = DateTime.UtcNow;

            StateFlags |= SwapStateFlags.IsPaymentBroadcast;
            RaiseUpdated();
        }

        public void SetPaymentConfirmed()
        {
            StateFlags |= SwapStateFlags.IsPaymentConfirmed;
            RaiseUpdated();
        }

        public void SetRefundSigned()
        {
            StateFlags |= SwapStateFlags.IsRefundSigned;
            RaiseUpdated();
        }

        public void SetRefundBroadcast()
        {
            if (RefundTx != null)
                RefundTx.BlockInfo.FirstSeen = DateTime.UtcNow;

            StateFlags |= SwapStateFlags.IsRefundBroadcast;
            RaiseUpdated();
        }

        public void SetRefundConfirmed()
        {
            StateFlags |= SwapStateFlags.IsRefundConfirmed;
            RaiseUpdated();
        }

        public void SetRedeemSigned()
        {
            StateFlags |= SwapStateFlags.IsRedeemSigned;
            RaiseUpdated();
        }

        public void SetRedeemBroadcast()
        {
            if (RedeemTx != null)
                RedeemTx.BlockInfo.FirstSeen = DateTime.UtcNow;

            StateFlags |= SwapStateFlags.IsRedeemBroadcast;
            RaiseUpdated();
        }

        public void SetRedeemSpent()
        {
            StateFlags |= SwapStateFlags.IsRedeemSpent;
            RaiseUpdated();
        }

        public void SetPartyPaymentSigned()
        {
            StateFlags |= SwapStateFlags.IsPartyPaymentSigned;
            RaiseUpdated();
        }

        public void SetPartyPaymentBroadcast()
        {
            if (PartyPaymentTx != null)
                PartyPaymentTx.BlockInfo.FirstSeen = DateTime.UtcNow;

            StateFlags |= SwapStateFlags.IsPartyPaymentBroadcast;
            RaiseUpdated();
        }

        public void SetPartyPaymentConfirmed()
        {
            StateFlags |= SwapStateFlags.IsPartyPaymentConfirmed;
            RaiseUpdated();
        }

        public void SetPartyRefundSigned()
        {
            StateFlags |= SwapStateFlags.IsPartyRefundSigned;
            RaiseUpdated();
        }

        public void SetPartyRedeemSpent()
        {
            StateFlags |= SwapStateFlags.IsPartyRedeemSpent;
            RaiseUpdated();
        }
    }
}