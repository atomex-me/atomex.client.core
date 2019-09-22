using Atomex.Blockchain.Tezos;
using LiteDB;

namespace Atomex.Common.Bson
{
    public class TezosTransactionToBsonSerializer : BsonSerializer<TezosTransaction>
    {
        public override void Register(BsonMapper bsonMapper)
        {
            bsonMapper.Entity<TezosTransaction>()
                .Ignore(tx => tx.Operations)
                .Ignore(tx => tx.Head)
                .Ignore(tx => tx.SignedMessage);
        }
    }
}