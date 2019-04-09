using System.Linq;
using System.Numerics;
using Atomix.Common.Bson;
using LiteDB;

namespace Atomix.Blockchain.Ethereum
{
    public class EthereumTransactionToBsonSerializer : BsonSerializer<EthereumTransaction>
    {
        private const string CurrencyKey = "currency";
        private const string FromKey = "from";
        private const string ToKey = "to";
        private const string InputKey = "input";
        private const string AmountKey = "amount";
        private const string NonceKey = "nonce";
        private const string GasPriceKey = "gasprice";
        private const string GasLimitKey = "gaslimit";
        private const string GasUsedKey = "gasused";
        private const string RlpEncodedKey = "rlpencoded";
        private const string TypeKey = "operationtype";
        private const string ReceiptStatusKey = "receiptstatus";
        private const string IsInternalKey = "internal";

        private const string FeesKey = "fees";
        private const string ConfirmationsKey = "confirmations";
        private const string BlockHeightKey = "blockheight";
        private const string FirstSeenKey = "firstseen";
        private const string BlockTimeKey = "blocktime";

        private const string InternalSuffix = "-internal";

        protected override EthereumTransaction Deserialize(BsonValue tx)
        {
            var bson = tx as BsonDocument;
            if (bson == null)
                return null;

            var currencyName = bson[CurrencyKey].IsString
                ? bson[CurrencyKey].AsString
                : string.Empty;

            var currency = Currencies.Available
                .FirstOrDefault(c => c.Name.Equals(currencyName));

            if (currency is Atomix.Ethereum)
            {
                return new EthereumTransaction
                {
                    Id = bson[IdKey].AsString.Replace(InternalSuffix, string.Empty),
                    From = bson[FromKey].AsString.ToLowerInvariant(),
                    To = bson[ToKey].AsString.ToLowerInvariant(),
                    Input = bson[InputKey].AsString,
                    Amount = new BigInteger(bson[AmountKey].AsBinary),
                    Nonce = new BigInteger(bson[NonceKey].AsBinary),
                    GasPrice = new BigInteger(bson[GasPriceKey].AsBinary),
                    GasLimit = new BigInteger(bson[GasLimitKey].AsBinary),
                    GasUsed = bson.ContainsKey(GasUsedKey)
                        ? new BigInteger(bson[GasUsedKey].AsBinary)
                        : 0,
                    RlpEncodedTx = bson[RlpEncodedKey].AsString,
                    Type = bson[TypeKey].AsInt32,
                    ReceiptStatus = !bson.ContainsKey(ReceiptStatusKey) || bson[ReceiptStatusKey].AsBoolean,
                    IsInternal = bson.ContainsKey(IsInternalKey) && bson[IsInternalKey].AsBoolean,

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

        protected override BsonValue Serialize(EthereumTransaction tx)
        {
            if (tx == null)
                return null;

            return new BsonDocument
            {
                [IdKey] = tx.Id + (tx.IsInternal ? InternalSuffix : string.Empty),
                [CurrencyKey] = tx.Currency.Name,
                [FromKey] = tx.From.ToLowerInvariant(),
                [ToKey] = tx.To.ToLowerInvariant(),
                [InputKey] = tx.Input,
                [AmountKey] = tx.Amount.ToByteArray(),
                [NonceKey] = tx.Nonce.ToByteArray(),
                [GasPriceKey] = tx.GasPrice.ToByteArray(),
                [GasLimitKey] = tx.GasLimit.ToByteArray(),
                [GasUsedKey] = tx.GasUsed.ToByteArray(),
                [RlpEncodedKey] = tx.RlpEncodedTx,
                [TypeKey] = tx.Type,
                [ReceiptStatusKey] = tx.ReceiptStatus,
                [IsInternalKey] = tx.IsInternal,

                [FeesKey] = tx.BlockInfo?.Fees,
                [ConfirmationsKey] = tx.BlockInfo?.Confirmations,
                [BlockHeightKey] = tx.BlockInfo?.BlockHeight,
                [FirstSeenKey] = tx.BlockInfo?.FirstSeen.ToLocalTime(), //.ToUniversalTime(),
                [BlockTimeKey] = tx.BlockInfo?.BlockTime.ToLocalTime(), //.ToUniversalTime()
            };
        }
    }
}