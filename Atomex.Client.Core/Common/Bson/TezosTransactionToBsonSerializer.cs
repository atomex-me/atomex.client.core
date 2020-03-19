using Atomex.Blockchain.Tezos;
using LiteDB;

namespace Atomex.Common.Bson
{
    public class TezosTransactionToBsonSerializer : BsonSerializer<TezosTransaction>
    {
        public override void Register(BsonMapper bsonMapper)
        {
            bsonMapper.Entity<TezosTransaction>()
                .Id(tx => tx.UniqueId)
                .Field(tx => tx.Id, "TxId")
                .Ignore(tx => tx.Operations)
                .Ignore(tx => tx.Head)
                .Ignore(tx => tx.SignedMessage);
        }
    }
}