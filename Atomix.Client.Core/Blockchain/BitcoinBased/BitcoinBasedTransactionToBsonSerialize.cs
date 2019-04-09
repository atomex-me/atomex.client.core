using System.Linq;
using Atomix.Common;
using Atomix.Common.Bson;
using LiteDB;
using NBitcoin;

namespace Atomix.Blockchain.BitcoinBased
{
    public class BitcoinBasedTransactionToBsonSerializer : BsonSerializer<BitcoinBasedTransaction>
    {
        private const string CurrencyKey = "currency";
        private const string TxKey = "tx";
        private const string FeesKey = "fees";
        private const string ConfirmationsKey = "confirmations";
        private const string BlockHeightKey = "blockheight";
        private const string FirstSeenKey = "firstseen";
        private const string BlockTimeKey = "blocktime";

        protected override BitcoinBasedTransaction Deserialize(BsonValue tx)
        {
            var bson = tx as BsonDocument;
            if (bson == null)
                return null;

            var currencyName = bson[CurrencyKey].IsString
                ? bson[CurrencyKey].AsString
                : string.Empty;

            var currency = Currencies.Available
                .FirstOrDefault(c => c.Name.Equals(currencyName));

            if (currency is BitcoinBasedCurrency btcBaseCurrency)
            {
                return new BitcoinBasedTransaction(
                    currency: btcBaseCurrency,
                    tx: Transaction.Parse(bson[TxKey].AsString, btcBaseCurrency.Network),
                    blockInfo: new BlockInfo
                    {
                        Fees = bson[FeesKey].AsInt64,
                        Confirmations = bson[ConfirmationsKey].AsInt32,
                        BlockHeight = bson[BlockHeightKey].AsInt32,
                        FirstSeen = bson[FirstSeenKey].AsDateTime.ToUniversalTime(),
                        BlockTime = bson[BlockTimeKey].AsDateTime.ToUniversalTime()
                    }
                );
            }

            return null;
        }

        protected override BsonValue Serialize(BitcoinBasedTransaction tx)
        {
            if (tx == null)
                return null;

            return new BsonDocument
            {
                [IdKey] = tx.Id,
                [CurrencyKey] = tx.Currency.Name,
                [TxKey] = tx.ToBytes().ToHexString(),
                [FeesKey] = tx.Fees,
                [ConfirmationsKey] = tx.Confirmations,
                [BlockHeightKey] = tx.BlockHeight,
                [FirstSeenKey] = tx.FirstSeen.ToLocalTime(),
                [BlockTimeKey] = tx.BlockTime.ToLocalTime()
            };
        }
    }
}