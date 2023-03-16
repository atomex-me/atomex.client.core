using LiteDB;
using NBitcoin;

namespace Atomex.Common.Bson
{
    public class CoinToBsonSerializer : BsonSerializer<Coin>
    {
        private const string OutputHash = "OutputHash";
        private const string OutputIndex = "OutputIndex";
        private const string Amount = "Amount";
        private const string ScriptPubKey = "ScriptPubKey";

        public override Coin Deserialize(BsonValue bsonValue)
        {
            return new Coin(
                fromTxHash: new uint256(bsonValue[OutputHash].AsString),
                fromOutputIndex: (uint)bsonValue[OutputIndex].AsInt32,
                amount: new Money(bsonValue[Amount].AsInt64, MoneyUnit.Satoshi),
                scriptPubKey: Script.FromHex(bsonValue[ScriptPubKey].AsString));
        }

        public override BsonValue Serialize(Coin obj)
        {
            return new BsonDocument
            {
                [OutputHash]   = obj.Outpoint.Hash.ToString(),
                [OutputIndex]  = (int)obj.Outpoint.N,
                [Amount]       = obj.Amount.Satoshi,
                [ScriptPubKey] = obj.ScriptPubKey.ToHex()
            };
        }
    }
}