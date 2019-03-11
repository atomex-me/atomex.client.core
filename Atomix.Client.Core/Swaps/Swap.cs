using System;
using Atomix.Blockchain.Abstract;
using Atomix.Core.Entities;
using Atomix.Swaps.Abstract;

namespace Atomix.Swaps
{
    public class Swap : ISwap
    {
        public Order Order { get; set; }
        public Guid Id => Order.SwapId;
        public SwapRequisites Requisites { get; set; }
        public SwapState State { get; set; }
        //public Symbol Symbol => Order.Symbol;
        public bool IsInitiator => Order.SwapInitiative;
        public bool IsCounterParty => !Order.SwapInitiative;

        public bool IsComplete => IsInitiator && State.HasFlag(SwapState.IsCounterPartyRedeemBroadcast) ||
                                  IsCounterParty && State.HasFlag(SwapState.IsInitiatorRedeemBroadcast);

        public bool IsRefunded => IsInitiator && State.HasFlag(SwapState.IsInitiatorRefundConfirmed) ||
                                  IsCounterParty && State.HasFlag(SwapState.IsCounterPartyRefundConfirmed);

        public bool IsCanceled => State.HasFlag(SwapState.IsCanceled);

        public byte[] Secret { get; set; }
        public byte[] SecretHash { get; set; }

        public string InitiatorPaymentTxId { get; set; }
        public string CounterPartyPaymentTxId { get; set; }

        public IBlockchainTransaction InitiatorPaymentTx { get; set; }
        public IBlockchainTransaction InitiatorPaymentSignedTx { get; set; }
        public IBlockchainTransaction InitiatorRefundTx { get; set; }
        public IBlockchainTransaction InitiatorRefundSignedTx { get; set; }
        public IBlockchainTransaction InitiatorRedeemTx { get; set; }
        public IBlockchainTransaction InitiatorRedeemSignedTx { get; set; }

        public IBlockchainTransaction CounterPartyPaymentTx { get; set; }
        public IBlockchainTransaction CounterPartyPaymentSignedTx { get; set; }
        public IBlockchainTransaction CounterPartyRefundTx { get; set; }
        public IBlockchainTransaction CounterPartyRefundSignedTx { get; set; }
        public IBlockchainTransaction CounterPartyRedeemTx { get; set; }
        public IBlockchainTransaction CounterPartyRedeemSignedTx { get; set; }

        public Swap() { }

        public Swap(Order order, SwapRequisites requisites)
        {
            Order = order ?? throw new ArgumentNullException(nameof(order));
            Requisites = requisites ?? throw new ArgumentNullException(nameof(requisites));
        }

        public void Reject()
        {
            State |= SwapState.IsCanceled;
        }

        public void SetSecret(byte[] secret)
        {
            Secret = secret;
            State |= SwapState.HasSecret;
        }

        public void SetSecretHash(byte[] secretHash)
        {
            SecretHash = secretHash;
            State |= SwapState.HasSecretHash;
        }

        public void SetInitiatorPaymentTx(IBlockchainTransaction tx)
        {
            InitiatorPaymentTx = tx;
            State |= SwapState.HasInitiatorPayment;
        }

        public void SetInitiatorPaymentTxId(string txId)
        {
            InitiatorPaymentTxId = txId;
            //State |= SwapState;
        }

        public void SetInitiatorPaymentSignedTx(IBlockchainTransaction tx)
        {
            InitiatorPaymentSignedTx = tx;
            State |= SwapState.HasInitiatorPaymentSigned;
        }

        public void SetInitiatorPaymentBroadcast()
        {
            if (InitiatorPaymentSignedTx != null)
                InitiatorPaymentSignedTx.BlockInfo.FirstSeen = DateTime.UtcNow;

            State |= SwapState.IsInitiatorPaymentBroadcast;
        }

        public void SetInitiatorPaymentConfirmed()
        {
            State |= SwapState.IsInitiatorPaymentConfirmed;
        }

        public void SetInitiatorRefundTx(IBlockchainTransaction tx)
        {
            InitiatorRefundTx = tx;
            State |= SwapState.HasInitiatorRefund;
        }

        public void SetInitiatorRefundSignedTx(IBlockchainTransaction tx)
        {
            InitiatorRefundSignedTx = tx;
            State |= SwapState.HasInitiatorRefundSigned;
        }

        public void SetInitiatorRefundBroadcast()
        {
            if (InitiatorRefundSignedTx != null)
                InitiatorRefundSignedTx.BlockInfo.FirstSeen = DateTime.UtcNow;

            State |= SwapState.IsInitiatorRefundBroadcast;
        }

        public void SetInitiatorRefundConfirmed()
        {
            State |= SwapState.IsInitiatorRefundConfirmed;
        }

        public void SetInitiatorRedeemTx(IBlockchainTransaction tx)
        {
            InitiatorRedeemTx = tx;
            State |= SwapState.HasInitiatorRedeem;
        }

        public void SetInitiatorRedeemSignedTx(IBlockchainTransaction tx)
        {
            InitiatorRedeemSignedTx = tx;
            State |= SwapState.HasInitiatorRedeemSigned;
        }

        public void SetInitiatorRedeemBroadcast()
        {
            if (InitiatorRedeemSignedTx != null)
                InitiatorRedeemSignedTx.BlockInfo.FirstSeen = DateTime.UtcNow;

            State |= SwapState.IsInitiatorRedeemBroadcast;
        }

        public void SetCounterPartyPaymentTx(IBlockchainTransaction tx)
        {
            CounterPartyPaymentTx = tx;
            State |= SwapState.HasCounterPartyPayment;
        }

        public void SetCounterPartyPaymentTxId(string txId)
        {
            CounterPartyPaymentTxId = txId;
            //State |= SwapState;
        }

        public void SetCounterPartyPaymentSignedTx(IBlockchainTransaction tx)
        {
            CounterPartyPaymentSignedTx = tx;
            State |= SwapState.HasCounterPartyPaymentSigned;
        }

        public void SetCounterPartyPaymentBroadcast()
        {
            if (CounterPartyPaymentSignedTx != null)
                CounterPartyPaymentSignedTx.BlockInfo.FirstSeen = DateTime.UtcNow;

            State |= SwapState.IsCounterPartyPaymentBroadcast;
        }

        public void SetCounterPartyPaymentConfirmed()
        {
            State |= SwapState.IsCounterPartyPaymentConfirmed;
        }

        public void SetCounterPartyRefundTx(IBlockchainTransaction tx)
        {
            CounterPartyRefundTx = tx;
            State |= SwapState.HasCounterPartyRefund;
        }

        public void SetCounterPartyRefundSignedTx(IBlockchainTransaction tx)
        {
            CounterPartyRefundSignedTx = tx;
            State |= SwapState.HasCounterPartyRefundSigned;
        }

        public void SetCounterPartyRefundBroadcast()
        {
            if (CounterPartyRefundSignedTx != null)
                CounterPartyRefundSignedTx.BlockInfo.FirstSeen = DateTime.UtcNow;

            State |= SwapState.IsCounterPartyRefundBroadcast;
        }

        public void SetCounterPartyRefundConfirmed()
        {
            State |= SwapState.IsCounterPartyRefundConfirmed;
        }

        public void SetCounterPartyRedeemTx(IBlockchainTransaction tx)
        {
            CounterPartyRedeemTx = tx;
            State |= SwapState.HasCounterPartyRedeem;
        }

        public void SetCounterPartyRedeemSignedTx(IBlockchainTransaction tx)
        {
            CounterPartyRedeemSignedTx = tx;
            State |= SwapState.HasCounterPartyRedeemSigned;
        }

        public void SetCounterPartyRedeemBroadcast()
        {
            if (CounterPartyRedeemSignedTx != null)
                CounterPartyRedeemSignedTx.BlockInfo.FirstSeen = DateTime.UtcNow;

            State |= SwapState.IsCounterPartyRedeemBroadcast;
        }
    }
}