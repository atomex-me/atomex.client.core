using System;

using LiteDB;

namespace Atomex.Common.Bson
{
    public abstract class BsonSerializer<T>
    {
        protected const string IdKey = "_id";

        protected BsonMapper BsonMapper { get; private set; }

        public virtual void Register(BsonMapper bsonMapper)
        {
            BsonMapper = bsonMapper ?? throw new ArgumentNullException(nameof(bsonMapper));

            BsonMapper.RegisterType(
                serialize: Serialize,
                deserialize: Deserialize);
        }

        public abstract T Deserialize(BsonValue bsonValue);
        public abstract BsonValue Serialize(T obj);
    }
}