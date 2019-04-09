using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using LiteDB;
using NBitcoin;

namespace Atomix.Common.Bson
{
    public class BitcoinBasedTxOutputToBsonSerializer : BsonSerializer<BitcoinBasedTxOutput>
    {
        private const string TxIdKey = "txid";
        private const string IndexKey = "index";
        private const string ValueKey = "value";
        private const string SpentHashKey = "spenthash";
        private const string SpentIndexKey = "spentindex";
        private const string ScriptKey = "script";

        protected override BitcoinBasedTxOutput Deserialize(BsonValue output)
        {
            var bson = output as BsonDocument;
            if (bson == null)
                return null;

            var spentHash = !bson[SpentHashKey].IsNull
                ? bson[SpentHashKey].AsString
                : null;

            var spentPoint = spentHash != null
                ? new TxPoint((uint)bson[SpentIndexKey].AsInt32, spentHash)
                : null;

            return new BitcoinBasedTxOutput(
                coin: new Coin(
                    fromTxHash: new uint256(bson[TxIdKey].AsString),
                    fromOutputIndex: (uint)bson[IndexKey].AsInt32,
                    amount: new Money(bson[ValueKey].AsInt64),
                    scriptPubKey: new Script(Hex.FromString(bson[ScriptKey].AsString))
                ),
                spentTxPoint: spentPoint
            );
        }

        protected override BsonValue Serialize(BitcoinBasedTxOutput output)
        {
            if (output == null)
                return null;

            return new BsonDocument
            {
                [IdKey] = $"{output.TxId}:{output.Index}",
                [TxIdKey] = output.TxId,
                [IndexKey] = (int)output.Index,
                [ValueKey] = output.Value,
                [SpentIndexKey] = output.SpentTxPoint != null
                    ? (int)output.SpentTxPoint?.Index
                    : 0,
                [SpentHashKey] = output.SpentTxPoint?.Hash,
                [ScriptKey] = output.Coin.TxOut.ScriptPubKey.ToHex()
            };
        }
    }
}