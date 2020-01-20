using Atomex.Core;
using LiteDB;

namespace Atomex.Common.Bson
{
    public class OrderToBsonSerializer : BsonSerializer<Order>
    {
        public override void Register(BsonMapper bsonMapper)
        {
            bsonMapper.Entity<Order>()
                .Id(p => p.ClientOrderId)
                .Field(p => p.Id, "OrderId");
        }
    }
}