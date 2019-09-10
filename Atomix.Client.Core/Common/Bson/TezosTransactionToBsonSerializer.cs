using Atomix.Common.Bson;
using LiteDB;

namespace Atomix.Blockchain.Tezos
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