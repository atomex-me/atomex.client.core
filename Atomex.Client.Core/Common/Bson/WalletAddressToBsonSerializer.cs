using Atomex.Core;
using LiteDB;

namespace Atomex.Common.Bson
{
    public class WalletAddressToBsonSerializer : BsonSerializer<WalletAddress>
    {
        public override void Register(BsonMapper bsonMapper)
        {
            bsonMapper.Entity<WalletAddress>()
                .Id(w => w.Address)
                .Ignore(w => w.Id)
                .Ignore(w => w.UserId)
                .Ignore(w => w.CurrencyId)
                .Ignore(w => w.PublicKey)
                .Ignore(w => w.ProofOfPossession)
                .Ignore(w => w.Nonce);
        }
    }
}
