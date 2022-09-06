using LiteDB;
using NBitcoin;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;

namespace Atomex.Common.Bson
{
    public class BitcoinBasedTxOutputToBsonSerializer : BsonSerializer<BitcoinBasedTxOutput>
    {
        private const string TxIdKey                 = nameof(BitcoinBasedTxOutput.TxId);
        private const string IndexKey                = nameof(BitcoinBasedTxOutput.Index);
        private const string ValueKey                = nameof(BitcoinBasedTxOutput.Value);
        private const string SpentHashKey            = "SpentHash";
        private const string SpentIndexKey           = "SpentIndex";
        private const string ScriptKey               = "Script";
        private const string ConfirmationsKey        = "Confirmations";
        private const string SpentTxConfirmationsKey = "SpentTxConfirmations";

        public override BitcoinBasedTxOutput Deserialize(BsonValue output)
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
                    scriptPubKey: Script.FromHex(bson[ScriptKey].AsString)
                ),
                confirmations: bson.ContainsKey(ConfirmationsKey)
                    ? bson[ConfirmationsKey].AsInt32
                    : 0,
                spentTxPoint: spentPoint,
                spentTxConfirmations: bson.ContainsKey(SpentTxConfirmationsKey)
                    ? bson[SpentTxConfirmationsKey].AsInt32
                    : 0
            );
        }

        public override BsonValue Serialize(BitcoinBasedTxOutput output)
        {
            if (output == null)
                return null;

            return new BsonDocument
            {
                [IdKey]         = $"{output.TxId}:{output.Index}",
                [TxIdKey]       = output.TxId,
                [IndexKey]      = (int)output.Index,
                [ValueKey]      = output.Value,
                [SpentIndexKey] = output.SpentTxPoint != null
                    ? (int)output.SpentTxPoint?.Index
                    : 0,
                [SpentHashKey]  = output.SpentTxPoint?.Hash,
                [ScriptKey]     = output.Coin.TxOut.ScriptPubKey.ToHex(),
                [ConfirmationsKey] = output.Confirmations,
                [SpentTxConfirmationsKey] = output.SpentTxConfirmations
            };
        }
    }
}