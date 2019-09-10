using Atomix.Core.Entities;
using LiteDB;

namespace Atomix.Common.Bson
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