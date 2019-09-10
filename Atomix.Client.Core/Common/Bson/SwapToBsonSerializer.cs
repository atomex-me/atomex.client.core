using System;
using Atomix.Abstract;
using Atomix.Blockchain.Abstract;
using Atomix.Core;
using Atomix.Core.Entities;
using LiteDB;

namespace Atomix.Common.Bson
{
    public class SwapToBsonSerializer : BsonSerializer<ClientSwap>
    {
        private const string StatusKey = nameof(ClientSwap.Status);
        private const string StateKey = nameof(ClientSwap.StateFlags);
        private const string TimeStampKey = nameof(ClientSwap.TimeStamp);
        private const string SymbolKey = nameof(ClientSwap.Symbol);
        private const string SideKey = nameof(ClientSwap.Side);
        private const string PriceKey = nameof(ClientSwap.Price);
        private const string QtyKey = nameof(ClientSwap.Qty);
        private const string IsInitiativeKey = nameof(ClientSwap.IsInitiative);
        private const string ToAddressKey = nameof(ClientSwap.ToAddress);
        private const string RewardForRedeemKey = nameof(ClientSwap.RewardForRedeem);
        private const string PaymentTxIdKey = nameof(ClientSwap.PaymentTxId);
        private const string PartyAddressKey = nameof(ClientSwap.PartyAddress);
        private const string PartyRewardForRedeemKey = nameof(ClientSwap.PartyRewardForRedeem);
        private const string PartyPaymentTxIdKey = nameof(ClientSwap.PartyPaymentTxId);

        private const string SecretKey = nameof(ClientSwap.Secret);
        private const string SecretHashKey = nameof(ClientSwap.SecretHash);

        private const string PaymentTxKey = nameof(ClientSwap.PaymentTx);
        private const string RefundTxKey = nameof(ClientSwap.RefundTx);
        private const string RedeemTxKey = nameof(ClientSwap.RedeemTx);
        private const string PartyPaymentTxKey = nameof(ClientSwap.PartyPaymentTx);

        private readonly ISymbols _symbols;

        public SwapToBsonSerializer(ISymbols symbols)
        {
            _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
        }

        public override ClientSwap Deserialize(BsonValue bsonValue)
        {
            var bson = bsonValue as BsonDocument;
            if (bson == null)
                return null;

            Enum.TryParse<SwapStatus>(bson[StatusKey].AsString, out var status);
            Enum.TryParse<SwapStateFlags>(bson[StateKey].AsString, out var state);
            Enum.TryParse<Side>(bson[SideKey].AsString, out var side);

            var symbol = _symbols.GetByName(bson[SymbolKey].AsString);
            var soldCurrency = symbol.SoldCurrency(side);
            var purchasedCurrency = symbol.PurchasedCurrency(side);

            return new ClientSwap
            {
                Id = bson[IdKey].AsInt64,
                Status = status,
                StateFlags = state,
                TimeStamp = bson[TimeStampKey].AsDateTime,
                Symbol = symbol,
                Side = side,
                Price = bson[PriceKey].AsDecimal,
                Qty = bson[QtyKey].AsDecimal,
                IsInitiative = bson[IsInitiativeKey].AsBoolean,
                ToAddress = bson[ToAddressKey].AsString,
                RewardForRedeem = bson[RewardForRedeemKey].AsDecimal,
                PaymentTxId = bson[PaymentTxIdKey].AsString,
                PartyAddress = bson[PartyAddressKey].AsString,
                PartyRewardForRedeem = bson[PartyRewardForRedeemKey].AsDecimal,
                PartyPaymentTxId = bson[PartyPaymentTxIdKey].AsString,

                Secret = bson[SecretKey].AsBinary,
                SecretHash = bson[SecretHashKey].AsBinary,

                PaymentTx = !bson[PaymentTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.ToObject(
                        type: soldCurrency.TransactionType,
                        doc: bson[PaymentTxKey].AsDocument)
                    : null,

                RefundTx = !bson[RefundTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.ToObject(
                        type: soldCurrency.TransactionType,
                        doc: bson[RefundTxKey].AsDocument)
                    : null,

                RedeemTx = !bson[RedeemTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.ToObject(
                        type: purchasedCurrency.TransactionType,
                        doc: bson[RedeemTxKey].AsDocument)
                    : null,

                PartyPaymentTx = !bson[PartyPaymentTxKey].IsNull
                    ? (IBlockchainTransaction)BsonMapper.ToObject(
                        type: purchasedCurrency.TransactionType,
                        doc: bson[PartyPaymentTxKey].AsDocument)
                    : null,
            };
        }

        public override BsonValue Serialize(ClientSwap swap)
        {
            return new BsonDocument
            {
                [IdKey] = swap.Id,
                [StatusKey] = swap.Status.ToString(),
                [StateKey] = swap.StateFlags.ToString(),
                [TimeStampKey] = swap.TimeStamp,
                [SymbolKey] = swap.Symbol.Name,
                [SideKey] = swap.Side.ToString(),
                [PriceKey] = swap.Price,
                [QtyKey] = swap.Qty,
                [IsInitiativeKey] = swap.IsInitiative,
                [ToAddressKey] = swap.ToAddress,
                [RewardForRedeemKey] = swap.RewardForRedeem,
                [PaymentTxIdKey] = swap.PaymentTxId,
                [PartyAddressKey] = swap.PartyAddress,
                [PartyRewardForRedeemKey] = swap.PartyRewardForRedeem,
                [PartyPaymentTxIdKey] = swap.PartyPaymentTxId,

                [SecretKey] = swap.Secret,
                [SecretHashKey] = swap.SecretHash,

                [PaymentTxKey] = swap.PaymentTx != null
                    ? BsonMapper.ToDocument(swap.PaymentTx)
                    : null,
                [RefundTxKey] = swap.RefundTx != null
                    ? BsonMapper.ToDocument(swap.RefundTx)
                    : null,
                [RedeemTxKey] = swap.RedeemTx != null
                    ? BsonMapper.ToDocument(swap.RedeemTx)
                    : null,
                [PartyPaymentTxKey] = swap.PartyPaymentTx != null
                    ? BsonMapper.ToDocument(swap.PartyPaymentTx)
                    : null,
            };
        }
    }
}