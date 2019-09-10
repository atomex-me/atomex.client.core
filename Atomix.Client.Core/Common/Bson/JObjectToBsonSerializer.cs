using LiteDB;
using Newtonsoft.Json.Linq;

namespace Atomix.Common.Bson
{
    public class JObjectToBsonSerializer : BsonSerializer<JObject>
    {
        public override JObject Deserialize(BsonValue bsonValue)
        {
            return !bsonValue.IsNull
                ? JObject.Parse(bsonValue.AsString)
                : null;
        }

        public override BsonValue Serialize(JObject o)
        {
            return o?.ToString();
        }
    }
}