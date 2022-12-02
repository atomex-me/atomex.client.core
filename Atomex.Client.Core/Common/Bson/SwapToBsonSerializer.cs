using System;
using System.Linq;

using LiteDB;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Core;
using Atomex.Client.V1.Entities;
using Swap = Atomex.Core.Swap;

namespace Atomex.Common.Bson
{
    public class SwapToBsonSerializer : BsonSerializer<Swap>
    {
        private const string StatusKey               = nameof(Swap.Status);
        private const string StateKey                = nameof(Swap.StateFlags);
        private const string TimeStampKey            = nameof(Swap.TimeStamp);
        private const string SymbolKey               = nameof(Swap.Symbol);
        private const string SideKey                 = nameof(Swap.Side);
        private const string PriceKey                = nameof(Swap.Price);
        private const string QtyKey                  = nameof(Swap.Qty);
        private const string IsInitiativeKey         = nameof(Swap.IsInitiative);

        private const string ToAddressKey            = nameof(Swap.ToAddress);
        private const string RewardForRedeemKey      = nameof(Swap.RewardForRedeem);
        private const string PaymentTxIdKey          = nameof(Swap.PaymentTxId);
        private const string RedeemScriptKey         = nameof(Swap.RedeemScript);
        private const string RefundAddressKey        = nameof(Swap.RefundAddress);
        private const string FromAddressKey          = nameof(Swap.FromAddress);
        private const string FromOutputsKey          = nameof(Swap.FromOutputs);
        private const string RedeemFromAddressKey    = nameof(Swap.RedeemFromAddress);

        private const string PartyAddressKey         = nameof(Swap.PartyAddress);
        private const string PartyRewardForRedeemKey = nameof(Swap.PartyRewardForRedeem);
        private const string PartyPaymentTxIdKey     = nameof(Swap.PartyPaymentTxId);
        private const string PartyRedeemScriptKey    = nameof(Swap.PartyRedeemScript);
        private const string PartyRefundAddressKey   = nameof(Swap.PartyRefundAddress);

        private const string OrderIdKey              = nameof(Swap.OrderId);

        private const string SecretKey               = nameof(Swap.Secret);
        private const string SecretHashKey           = nameof(Swap.SecretHash);

        private const string PaymentTxKey            = nameof(Swap.PaymentTx);
        private const string RefundTxKey             = nameof(Swap.RefundTx);
        private const string RedeemTxKey             = nameof(Swap.RedeemTx);
        private const string PartyPaymentTxKey       = nameof(Swap.PartyPaymentTx);

        private const string MakerNetworkFeeKey      = nameof(Swap.MakerNetworkFee);

        private readonly ICurrencies _currencies;

        public SwapToBsonSerializer(ICurrencies currencies)
        {
            _currencies = currencies ?? throw new ArgumentNullException(nameof(currencies));
        }

        public override Swap Deserialize(BsonValue bsonValue)
        {
            var bson = bsonValue as BsonDocument;
            if (bson == null)
                return null;

            Enum.TryParse<SwapStatus>(bson[StatusKey].AsString, out var status);
            Enum.TryParse<SwapStateFlags>(bson[StateKey].AsString, out var state);
            Enum.TryParse<Side>(bson[SideKey].AsString, out var side);

            var symbol = bson[SymbolKey].AsString;
            var soldCurrency = _currencies.GetByName(symbol.SoldCurrency(side));
            var purchasedCurrency = _currencies.GetByName(symbol.PurchasedCurrency(side));

            var fromOutputs = !bson[FromOutputsKey].IsNull
                ? bson[FromOutputsKey].AsArray
                    .Select(v => BsonMapper.ToObject<BitcoinTxOutput>((BsonDocument)v))
                    .ToList()
                : null;

            return new Swap
            {
                Id                   = bson[IdKey].AsInt64,
                OrderId              = !bson[OrderIdKey].IsNull ? bson[OrderIdKey].AsInt64 : 0,
                Status               = status,
                StateFlags           = state,
                TimeStamp            = bson[TimeStampKey].AsDateTime,
                Symbol               = bson[SymbolKey].AsString,
                Side                 = side,
                Price                = bson[PriceKey].AsDecimal,
                Qty                  = bson[QtyKey].AsDecimal,
                IsInitiative         = bson[IsInitiativeKey].AsBoolean,

                ToAddress            = bson[ToAddressKey].AsString,
                RewardForRedeem      = bson[RewardForRedeemKey].AsDecimal,
                PaymentTxId          = bson[PaymentTxIdKey].AsString,
                RedeemScript         = bson[RedeemScriptKey].AsString,
                RefundAddress        = bson[RefundAddressKey].AsString,
                FromAddress          = bson[FromAddressKey].AsString,
                FromOutputs          = fromOutputs,
                RedeemFromAddress    = bson[RedeemFromAddressKey].AsString,

                PartyAddress         = bson[PartyAddressKey].AsString,
                PartyRewardForRedeem = bson[PartyRewardForRedeemKey].AsDecimal,
                PartyPaymentTxId     = bson[PartyPaymentTxIdKey].AsString,
                PartyRedeemScript    = bson[PartyRedeemScriptKey].AsString,
                PartyRefundAddress   = bson[PartyRefundAddressKey].AsString,

                MakerNetworkFee      = !bson[MakerNetworkFeeKey].IsNull ? bson[MakerNetworkFeeKey].AsDecimal : 0m,
                Secret               = bson[SecretKey].AsBinary,
                SecretHash           = bson[SecretHashKey].AsBinary,

                PaymentTx = !bson[PaymentTxKey].IsNull
                    ? (ITransaction)BsonMapper.ToObject(
                        type: soldCurrency.TransactionType,
                        doc: bson[PaymentTxKey].AsDocument)
                    : null,

                RefundTx = !bson[RefundTxKey].IsNull
                    ? (ITransaction)BsonMapper.ToObject(
                        type: soldCurrency.TransactionType,
                        doc: bson[RefundTxKey].AsDocument)
                    : null,

                RedeemTx = !bson[RedeemTxKey].IsNull
                    ? (ITransaction)BsonMapper.ToObject(
                        type: purchasedCurrency.TransactionType,
                        doc: bson[RedeemTxKey].AsDocument)
                    : null,

                PartyPaymentTx = !bson[PartyPaymentTxKey].IsNull
                    ? (ITransaction)BsonMapper.ToObject(
                        type: purchasedCurrency.TransactionType,
                        doc: bson[PartyPaymentTxKey].AsDocument)
                    : null,
            };
        }

        public override BsonValue Serialize(Swap swap)
        {
            var bsonFromOutputs = swap.FromOutputs != null
                ? new BsonArray(swap.FromOutputs.Select(o => BsonMapper.ToDocument(o)))
                : null;

            return new BsonDocument
            {
                [IdKey]                   = swap.Id,
                [OrderIdKey]              = swap.OrderId,
                [StatusKey]               = swap.Status.ToString(),
                [StateKey]                = swap.StateFlags.ToString(),
                [TimeStampKey]            = swap.TimeStamp,
                [SymbolKey]               = swap.Symbol,
                [SideKey]                 = swap.Side.ToString(),
                [PriceKey]                = swap.Price,
                [QtyKey]                  = swap.Qty,
                [IsInitiativeKey]         = swap.IsInitiative,

                [ToAddressKey]            = swap.ToAddress,
                [RewardForRedeemKey]      = swap.RewardForRedeem,
                [PaymentTxIdKey]          = swap.PaymentTxId,
                [RedeemScriptKey]         = swap.RedeemScript,
                [RefundAddressKey]        = swap.RefundAddress,
                [FromAddressKey]          = swap.FromAddress,
                [FromOutputsKey]          = bsonFromOutputs,
                [RedeemFromAddressKey]    = swap.RedeemFromAddress,

                [PartyAddressKey]         = swap.PartyAddress,
                [PartyRewardForRedeemKey] = swap.PartyRewardForRedeem,
                [PartyPaymentTxIdKey]     = swap.PartyPaymentTxId,
                [PartyRedeemScriptKey]    = swap.PartyRedeemScript,
                [PartyRefundAddressKey]   = swap.PartyRefundAddress,

                [MakerNetworkFeeKey]      = swap.MakerNetworkFee,
                [SecretKey]               = swap.Secret,
                [SecretHashKey]           = swap.SecretHash,

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