using LiteDB;

using Atomex.Core;

namespace Atomex.Common.Bson
{
    public class WalletAddressToBsonSerializer : BsonSerializer<WalletAddress>
    {
        public override void Register(BsonMapper bsonMapper)
        {
            bsonMapper.Entity<WalletAddress>()
                .Ignore(w => w.PublicKey)
                .Ignore(w => w.ProofOfPossession)
                .Ignore(w => w.Nonce);
        }
    }
}
