using LiteDB;
using System;

namespace Atomix.Common.Bson
{
    public class DateTimeToBsonSerializer : BsonSerializer<DateTime>
    {
        public override DateTime Deserialize(BsonValue value)
        {
            return value.AsDateTime.ToUniversalTime();
        }

        public override BsonValue Serialize(DateTime dateTime)
        {
            return dateTime.ToLocalTime();
        }
    }
}