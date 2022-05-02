using LiteDB;

using Atomex.Blockchain.Tezos;

namespace Atomex.Common.Bson
{
    public class TezosTransactionToBsonSerializer : BsonSerializer<TezosTransaction_OLD>
    {
        public override void Register(BsonMapper bsonMapper)
        {
            bsonMapper.Entity<TezosTransaction_OLD>()
                .Id(tx => tx.UniqueId)
                .Field(tx => tx.Id, "TxId")
                .Ignore(tx => tx.Operations)
                .Ignore(tx => tx.Head)
                .Ignore(tx => tx.SignedMessage);
        }
    }
}