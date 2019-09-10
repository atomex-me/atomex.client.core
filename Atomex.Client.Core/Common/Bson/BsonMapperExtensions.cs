using LiteDB;

namespace Atomex.Common.Bson
{
    public static class BsonMapperExtensions
    {
        public static BsonMapper UseSerializer<T>(
            this BsonMapper bsonMapper,
            BsonSerializer<T> serializer)
        {
            serializer.Register(bsonMapper);
            return bsonMapper;
        }
    }
}