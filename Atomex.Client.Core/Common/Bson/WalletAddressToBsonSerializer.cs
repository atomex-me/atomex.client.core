using Atomex.Core;
using LiteDB;

namespace Atomex.Common.Bson
{
    public class WalletAddressToBsonSerializer : BsonSerializer<WalletAddress>
    {
        public override void Register(BsonMapper bsonMapper)
        {
            bsonMapper.Entity<WalletAddress>()
                .Id(w => w.UniqueId)
                .Ignore(w => w.Id)
                .Ignore(w => w.PublicKey)
                .Ignore(w => w.ProofOfPossession)
                .Ignore(w => w.Nonce);
        }
    }
}
