using System;
using LiteDB;

namespace Atomix.Common.Bson
{
    public class BsonSerializer<T>
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

        public virtual T Deserialize(BsonValue bsonValue)
        {
            throw new NotImplementedException();
        }

        public virtual BsonValue Serialize(T obj)
        {
            throw new NotImplementedException();
        }
    }
}