using LiteDB;

using Atomex.Core;

namespace Atomex.Common.Bson
{
    public class WalletAddressToBsonSerializer : BsonSerializer<WalletAddress>
    {
        public override void Register(BsonMapper bsonMapper)
        {
            bsonMapper.Entity<WalletAddress>()
                .Id(w => w.UniqueId)
                .Ignore(w => w.Id);
        }
    }
}
