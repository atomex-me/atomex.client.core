using System.Numerics;

using LiteDB;

namespace Atomex.Common.Bson
{
    public class BigIntegerToBsonSerializer : BsonSerializer<BigInteger>
    {
        public override BigInteger Deserialize(BsonValue value) =>
            BigInteger.Parse(value.AsString);

        public override BsonValue Serialize(BigInteger value) =>
            new(value.ToString());
    }
}