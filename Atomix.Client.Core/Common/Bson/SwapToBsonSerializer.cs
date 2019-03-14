using Atomix.Blockchain.Abstract;
using Atomix.Core.Entities;
using Atomix.Swaps;
using Atomix.Swaps.Abstract;
using LiteDB;
using System;

namespace Atomix.Common.Bson
{
    public class SwapToBsonSerializer : BsonSerializer<SwapState>
    {
        private const string OrderKey = nameof(SwapState.Order);
        private const string RequisitesKey = nameof(SwapState.Requisites);
        private const string StateKey = nameof(SwapState.StateFlags);
        private const string SecretKey = nameof(SwapState.Secret);
        private const string SecretHashKey = nameof(SwapState.SecretHash);

        private const string PaymentTxIdKey = nameof(SwapState.PaymentTxId);
        private const string PartyPaymentTxIdKey = nameof(SwapState.PartyPaymentTxId);

        private const string PaymentTxKey = nameof(SwapState.PaymentTx);
        private const string RefundTxKey = nameof(SwapState.RefundTx);
        private const string RedeemTxKey = nameof(SwapState.RedeemTx);

        private const string PartyPaymentTxKey = nameof(SwapState.PartyPaymentTx);
        private const string PartyRefundTxKey = nameof(SwapState.PartyRefundTx);
        private const string PartyRedeemTxKey = nameof(SwapState.PartyRedeemTx);

        protected override SwapState Deserialize(BsonValue swap)
        {
            var bson = swap as BsonDocument;
            if (bson == null)
                return null;

            var order = BsonMapper.Global.ToObject<Order>(bson[OrderKey].AsDocument);
            var soldCurrency = order.SoldCurrency();
            var purchasedCurrency = order.PurchasedCurrency();

            Enum.TryParse<SwapStateFlags>(bson[StateKey].AsString, out var state);

            return new SwapState
            {
                Order = order,
                Requisites = BsonMapper.Global.ToObject<SwapRequisites>(bson[RequisitesKey].AsDocument),
                StateFlags = state,
                Secret = bson[SecretKey].AsBinary,
                SecretHash = bson[SecretHashKey].AsBinary,

                PaymentTxId = bson[PaymentTxIdKey].AsString,
                PartyPaymentTxId = bson[PartyPaymentTxIdKey].AsString,

                PaymentTx = !bson[PaymentTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: soldCurrency.TransactionType,
                        doc: bson[PaymentTxKey].AsDocument)
                    : null,

                RefundTx = !bson[RefundTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: soldCurrency.TransactionType,
                        doc: bson[RefundTxKey].AsDocument)
                    : null,

                RedeemTx = !bson[RedeemTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: purchasedCurrency.TransactionType,
                        doc: bson[RedeemTxKey].AsDocument)
                    : null,

                PartyPaymentTx = !bson[PartyPaymentTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: purchasedCurrency.TransactionType,
                        doc: bson[PartyPaymentTxKey].AsDocument)
                    : null,

                PartyRefundTx = !bson[PartyRefundTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: purchasedCurrency.TransactionType,
                        doc: bson[PartyRefundTxKey].AsDocument)
                    : null,

                PartyRedeemTx = !bson[PartyRedeemTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: soldCurrency.TransactionType,
                        doc: bson[PartyRedeemTxKey].AsDocument)
                    : null,
            };
        }

        protected override BsonValue Serialize(SwapState swapState)
        {
            return new BsonDocument
            {
                [IdKey] = swapState.Id,
                [OrderKey] = BsonMapper.Global.ToDocument(swapState.Order),
                [RequisitesKey] = BsonMapper.Global.ToDocument(swapState.Requisites),
                [StateKey] = swapState.StateFlags.ToString(),
                [SecretKey] = swapState.Secret,
                [SecretHashKey] = swapState.SecretHash,

                [PaymentTxIdKey] = swapState.PaymentTxId,
                [PartyPaymentTxIdKey] = swapState.PartyPaymentTxId,

                [PaymentTxKey] = swapState.PaymentTx != null
                    ? BsonMapper.Global.ToDocument(swapState.PaymentTx)
                    : null,
                [RefundTxKey] = swapState.RefundTx != null
                    ? BsonMapper.Global.ToDocument(swapState.RefundTx)
                    : null,
                [RedeemTxKey] = swapState.RedeemTx != null
                    ? BsonMapper.Global.ToDocument(swapState.RedeemTx)
                    : null,

                [PartyPaymentTxKey] = swapState.PartyPaymentTx != null
                    ? BsonMapper.Global.ToDocument(swapState.PartyPaymentTx)
                    : null,
                [PartyRefundTxKey] = swapState.PartyRefundTx != null
                    ? BsonMapper.Global.ToDocument(swapState.PartyRefundTx)
                    : null,
                [PartyRedeemTxKey] = swapState.PartyRedeemTx != null
                    ? BsonMapper.Global.ToDocument(swapState.PartyRedeemTx)
                    : null,
            };
        }
    }
}