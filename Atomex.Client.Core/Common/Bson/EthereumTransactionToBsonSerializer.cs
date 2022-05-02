using LiteDB;

using Atomex.Blockchain.Ethereum;

namespace Atomex.Common.Bson
{
    public class EthereumTransactionToBsonSerializer : BsonSerializer<EthereumTransaction_OLD>
    {
        public override void Register(BsonMapper bsonMapper)
        {
            bsonMapper.Entity<EthereumTransaction_OLD>()
                .Id(tx => tx.UniqueId)
                .Field(tx => tx.Id, "TxId");
        }
    }
}