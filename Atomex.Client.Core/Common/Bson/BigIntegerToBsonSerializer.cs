using System.Numerics;
using LiteDB;

namespace Atomex.Common.Bson
{
    public class BigIntegerToBsonSerializer : BsonSerializer<BigInteger>
    {
        public override BigInteger Deserialize(BsonValue value)
        {
            return new BigInteger(value.AsBinary);
        }

        public override BsonValue Serialize(BigInteger value)
        {
            return value.ToByteArray();
        }
    }
}