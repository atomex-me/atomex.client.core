using Atomix.Blockchain.Abstract;
using Atomix.Core.Entities;
using Atomix.Swaps;
using Atomix.Swaps.Abstract;
using LiteDB;
using System;

namespace Atomix.Common.Bson
{
    public class SwapToBsonSerializer : BsonSerializer<Swap>
    {
        private const string OrderKey = nameof(Swap.Order);
        private const string RequisitesKey = nameof(Swap.Requisites);
        private const string StateKey = nameof(Swap.State);
        private const string SecretKey = nameof(Swap.Secret);
        private const string SecretHashKey = nameof(Swap.SecretHash);

        private const string InitiatorPaymentTxIdKey = nameof(Swap.InitiatorPaymentTxId);
        private const string CounterPartyPaymentTxIdKey = nameof(Swap.CounterPartyPaymentTxId);

        private const string InitiatorPaymentTxKey = nameof(Swap.InitiatorPaymentTx);
        private const string InitiatorPaymentSignedTxKey = nameof(Swap.InitiatorPaymentSignedTx);
        private const string InitiatorRefundTxKey = nameof(Swap.InitiatorRefundTx);
        private const string InitiatorRefundSignedTxKey = nameof(Swap.InitiatorRefundSignedTx);
        private const string InitiatorRedeemTxKey = nameof(Swap.InitiatorRedeemTx);
        private const string InitiatorRedeemSignedTxKey = nameof(Swap.InitiatorRedeemSignedTx);

        private const string CounterPartyPaymentTxKey = nameof(Swap.CounterPartyPaymentTx);
        private const string CounterPartyPaymentSignedTxKey = nameof(Swap.CounterPartyPaymentSignedTx);
        private const string CounterPartyRefundTxKey = nameof(Swap.CounterPartyRefundTx);
        private const string CounterPartyRefundSignedTxKey = nameof(Swap.CounterPartyRefundSignedTx);
        private const string CounterPartyRedeemTxKey = nameof(Swap.CounterPartyRedeemTx);
        private const string CounterPartyRedeemSignedTxKey = nameof(Swap.CounterPartyRedeemSignedTx);

        protected override Swap Deserialize(BsonValue swap)
        {
            var bson = swap as BsonDocument;
            if (bson == null)
                return null;

            var order = BsonMapper.Global.ToObject<Order>(bson[OrderKey].AsDocument);

            var initiatorCurrency = order.SwapInitiative
                ? order.SoldCurrency()
                : order.PurchasedCurrency();

            var counterPartyCurrency = order.SwapInitiative
                ? order.PurchasedCurrency()
                : order.SoldCurrency();

            //var state = (SwapState) int.Parse(bson[StateKey].AsString);

            Enum.TryParse<SwapState>(bson[StateKey].AsString, out var state);

            return new Swap
            {
                Order = order,
                Requisites = BsonMapper.Global.ToObject<SwapRequisites>(bson[RequisitesKey].AsDocument),
                State = state,
                Secret = bson[SecretKey].AsBinary,
                SecretHash = bson[SecretHashKey].AsBinary,

                InitiatorPaymentTxId = bson[InitiatorPaymentTxIdKey].AsString,
                CounterPartyPaymentTxId = bson[CounterPartyPaymentTxIdKey].AsString,

                InitiatorPaymentTx = !bson[InitiatorPaymentTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: initiatorCurrency.TransactionType,
                        doc: bson[InitiatorPaymentTxKey].AsDocument)
                    : null,

                InitiatorPaymentSignedTx = !bson[InitiatorPaymentSignedTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: initiatorCurrency.TransactionType,
                        doc: bson[InitiatorPaymentSignedTxKey].AsDocument)
                    : null,

                InitiatorRefundTx = !bson[InitiatorRefundTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: initiatorCurrency.TransactionType,
                        doc: bson[InitiatorRefundTxKey].AsDocument)
                    : null,

                InitiatorRefundSignedTx = !bson[InitiatorRefundSignedTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: initiatorCurrency.TransactionType,
                        doc: bson[InitiatorRefundSignedTxKey].AsDocument)
                    : null,

                InitiatorRedeemTx = !bson[InitiatorRedeemTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: initiatorCurrency.TransactionType,
                        doc: bson[InitiatorRedeemTxKey].AsDocument)
                    : null,

                InitiatorRedeemSignedTx = !bson[InitiatorRedeemSignedTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: initiatorCurrency.TransactionType,
                        doc: bson[InitiatorRedeemSignedTxKey].AsDocument)
                    : null,

                CounterPartyPaymentTx = !bson[CounterPartyPaymentTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: counterPartyCurrency.TransactionType,
                        doc: bson[CounterPartyPaymentTxKey].AsDocument)
                    : null,

                CounterPartyPaymentSignedTx = !bson[CounterPartyPaymentSignedTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: counterPartyCurrency.TransactionType,
                        doc: bson[CounterPartyPaymentSignedTxKey].AsDocument)
                    : null,

                CounterPartyRefundTx = !bson[CounterPartyRefundTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: counterPartyCurrency.TransactionType,
                        doc: bson[CounterPartyRefundTxKey].AsDocument)
                    : null,

                CounterPartyRefundSignedTx = !bson[CounterPartyRefundSignedTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: counterPartyCurrency.TransactionType,
                        doc: bson[CounterPartyRefundSignedTxKey].AsDocument)
                    : null,

                CounterPartyRedeemTx = !bson[CounterPartyRedeemTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: counterPartyCurrency.TransactionType,
                        doc: bson[CounterPartyRedeemTxKey].AsDocument)
                    : null,

                CounterPartyRedeemSignedTx = !bson[CounterPartyRedeemSignedTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.Global.ToObject(
                        type: counterPartyCurrency.TransactionType,
                        doc: bson[CounterPartyRedeemSignedTxKey].AsDocument)
                    : null
            };
        }

        protected override BsonValue Serialize(Swap swap)
        {
            return new BsonDocument
            {
                [IdKey] = swap.Id,
                [OrderKey] = BsonMapper.Global.ToDocument(swap.Order),
                [RequisitesKey] = BsonMapper.Global.ToDocument(swap.Requisites),
                [StateKey] = swap.State.ToString(),
                [SecretKey] = swap.Secret,
                [SecretHashKey] = swap.SecretHash,

                [InitiatorPaymentTxIdKey] = swap.InitiatorPaymentTxId,
                [CounterPartyPaymentTxIdKey] = swap.CounterPartyPaymentTxId,

                [InitiatorPaymentTxKey] = swap.InitiatorPaymentTx != null
                    ? BsonMapper.Global.ToDocument(swap.InitiatorPaymentTx)
                    : null,
                [InitiatorPaymentSignedTxKey] = swap.InitiatorPaymentSignedTx != null 
                    ? BsonMapper.Global.ToDocument(swap.InitiatorPaymentSignedTx)
                    : null,
                [InitiatorRefundTxKey] = swap.InitiatorRefundTx != null
                    ? BsonMapper.Global.ToDocument(swap.InitiatorRefundTx)
                    : null,
                [InitiatorRefundSignedTxKey] = swap.InitiatorRefundSignedTx != null
                    ? BsonMapper.Global.ToDocument(swap.InitiatorRefundSignedTx)
                    : null,
                [InitiatorRedeemTxKey] = swap.InitiatorRedeemTx != null
                    ? BsonMapper.Global.ToDocument(swap.InitiatorRedeemTx)
                    : null,
                [InitiatorRedeemSignedTxKey] = swap.InitiatorRedeemSignedTx != null
                    ? BsonMapper.Global.ToDocument(swap.InitiatorRedeemSignedTx)
                    : null,

                [CounterPartyPaymentTxKey] = swap.CounterPartyPaymentTx != null
                    ? BsonMapper.Global.ToDocument(swap.CounterPartyPaymentTx)
                    : null,
                [CounterPartyPaymentSignedTxKey] = swap.CounterPartyPaymentSignedTx != null
                    ? BsonMapper.Global.ToDocument(swap.CounterPartyPaymentSignedTx)
                    : null,
                [CounterPartyRefundTxKey] = swap.CounterPartyRefundTx != null
                    ? BsonMapper.Global.ToDocument(swap.CounterPartyRefundTx)
                    : null,
                [CounterPartyRefundSignedTxKey] = swap.CounterPartyRefundSignedTx != null
                    ? BsonMapper.Global.ToDocument(swap.CounterPartyRefundSignedTx)
                    : null,
                [CounterPartyRedeemTxKey] = swap.CounterPartyRedeemTx != null
                    ? BsonMapper.Global.ToDocument(swap.CounterPartyRedeemTx)
                    : null,
                [CounterPartyRedeemSignedTxKey] = swap.CounterPartyRedeemSignedTx != null
                    ? BsonMapper.Global.ToDocument(swap.CounterPartyRedeemSignedTx)
                    : null,
            };
        }
    }
}