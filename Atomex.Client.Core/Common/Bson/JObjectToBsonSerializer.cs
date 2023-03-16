using LiteDB;
using Newtonsoft.Json.Linq;

namespace Atomex.Common.Bson
{
    public class JObjectToBsonSerializer : BsonSerializer<JObject>
    {
        public override JObject Deserialize(BsonValue bsonValue) =>
            !bsonValue.IsNull
                ? JObject.Parse(bsonValue.AsString)
                : null;

        public override BsonValue Serialize(JObject o) =>
            o?.ToString();
    }
}