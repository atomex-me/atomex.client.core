using System.Collections.Generic;
using System.Linq;
using Atomix.Common;
using Atomix.Common.Bson;
using Atomix.Core.Entities;
using LiteDB;
using NBitcoin;

namespace Atomix.Blockchain.BitcoinBased
{
    public class BitcoinBasedTransactionToBsonSerializer : BsonSerializer<BitcoinBasedTransaction>
    {
        private const string CurrencyKey = nameof(BitcoinBasedTransaction.Currency);
        private const string TxKey = nameof(BitcoinBasedTransaction.Tx);
        private const string BlockInfoKey = nameof(BitcoinBasedTransaction.BlockInfo);

        private readonly IEnumerable<Currency> _currencies;

        public BitcoinBasedTransactionToBsonSerializer(IEnumerable<Currency> currencies)
        {
            _currencies = currencies;
        }

        public override BitcoinBasedTransaction Deserialize(BsonValue tx)
        {
            var bson = tx as BsonDocument;
            if (bson == null)
                return null;

            var currencyName = bson[CurrencyKey].IsString
                ? bson[CurrencyKey].AsString
                : string.Empty;

            var currency = _currencies.FirstOrDefault(c => c.Name.Equals(currencyName));

            if (currency is BitcoinBasedCurrency btcBaseCurrency)
            {
                return new BitcoinBasedTransaction(
                    currency: btcBaseCurrency,
                    tx: Transaction.Parse(bson[TxKey].AsString, btcBaseCurrency.Network),
                    blockInfo: !bson[BlockInfoKey].IsNull
                        ? BsonMapper.ToObject<BlockInfo>(bson[BlockInfoKey].AsDocument)
                        : null
                );
            }

            return null;
        }

        public override BsonValue Serialize(BitcoinBasedTransaction tx)
        {
            if (tx == null)
                return null;

            return new BsonDocument
            {
                [IdKey] = tx.Id,
                [CurrencyKey] = tx.Currency.Name,
                [TxKey] = tx.ToBytes().ToHexString(),
                [BlockInfoKey] = tx.BlockInfo != null
                    ? BsonMapper.ToDocument(tx.BlockInfo)
                    : null
            };
        }
    }
}