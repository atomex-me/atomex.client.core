using LiteDB;

namespace Atomix.Common.Bson
{
    public abstract class BsonSerializer<T>
    {
        public const string IdKey = "_id";

        public void Register()
        {
            BsonMapper.Global.RegisterType(
                serialize: Serialize,
                deserialize: Deserialize);
        }

        protected abstract T Deserialize(BsonValue bsonValue);

        protected abstract BsonValue Serialize(T obj);
    }
}