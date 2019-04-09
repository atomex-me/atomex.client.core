using System.Linq;
using Atomix.Common.Bson;
using LiteDB;
using Newtonsoft.Json.Linq;

namespace Atomix.Blockchain.Tezos
{
    public class TezosTransactionToBsonSerializer : BsonSerializer<TezosTransaction>
    {
        private const string CurrencyKey = "currency";
        private const string FromKey = "from";
        private const string ToKey = "to";
        private const string AmountKey = "amount";
        private const string FeeKey = "fee";
        private const string GasLimitKey = "gaslimit";
        private const string StorageLimitKey = "storagelimit";
        private const string ParamsKey = "params";
        private const string TypeKey = "type";
        private const string InternalKey = "internal";

        private const string FeesKey = "fees";
        private const string ConfirmationsKey = "confirmations";
        private const string BlockHeightKey = "blockheight";
        private const string FirstSeenKey = "firstseen";
        private const string BlockTimeKey = "blocktime";

        private const string InternalSuffix = "-internal";

        protected override TezosTransaction Deserialize(BsonValue tx)
        {
            var bson = tx as BsonDocument;
            if (bson == null)
                return null;

            var currencyName = bson[CurrencyKey].IsString
                ? bson[CurrencyKey].AsString
                : string.Empty;

            var currency = Currencies.Available
                .FirstOrDefault(c => c.Name.Equals(currencyName));

            if (currency is Atomix.Tezos)
            {
                return new TezosTransaction
                {
                    Id = bson[IdKey].AsString.Replace(InternalSuffix, string.Empty),
                    From = bson[FromKey].AsString,
                    To = bson[ToKey].AsString,
                    Amount = bson[AmountKey].AsDecimal,
                    Fee = bson[FeeKey].AsDecimal,
                    GasLimit = bson[GasLimitKey].AsDecimal,
                    StorageLimit = bson.ContainsKey(StorageLimitKey)
                        ? bson[StorageLimitKey].AsDecimal
                        : 0,
                    Params = bson[ParamsKey].IsString
                        ? JObject.Parse(bson[ParamsKey].AsString)
                        : null,
                    Type = bson[TypeKey].AsInt32,
                    IsInternal = bson.ContainsKey(InternalKey) && bson[InternalKey].AsBoolean,

                    BlockInfo = new BlockInfo
                    {
                        Fees = bson[FeesKey].AsInt64,
                        Confirmations = bson[ConfirmationsKey].AsInt32,
                        BlockHeight = bson[BlockHeightKey].AsInt32,
                        FirstSeen = bson[FirstSeenKey].AsDateTime.ToUniversalTime(),
                        BlockTime = bson[BlockTimeKey].AsDateTime.ToUniversalTime(),
                    },
                };
            }

            return null;
        }

        protected override BsonValue Serialize(TezosTransaction tx)
        {
            if (tx == null)
                return null;

            return new BsonDocument
            {
                [IdKey] = tx.Id + (tx.IsInternal ? InternalSuffix : string.Empty),
                [CurrencyKey] = tx.Currency.Name,
                [FromKey] = tx.From,
                [ToKey] = tx.To,
                [AmountKey] = tx.Amount,
                [FeeKey] = tx.Fee,
                [GasLimitKey] = tx.GasLimit,
                [StorageLimitKey] = tx.StorageLimit,
                [ParamsKey] = tx.Params?.ToString(),
                [TypeKey] = tx.Type,
                [InternalKey] = tx.IsInternal,

                [FeesKey] = tx.BlockInfo?.Fees,
                [ConfirmationsKey] = tx.BlockInfo?.Confirmations,
                [BlockHeightKey] = tx.BlockInfo?.BlockHeight,
                [FirstSeenKey] = tx.BlockInfo?.FirstSeen.ToLocalTime(),
                [BlockTimeKey] = tx.BlockInfo?.BlockTime.ToLocalTime()
            };
        }
    }
}