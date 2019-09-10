using System;
using Atomix.Common.Bson;
using LiteDB;
using Xunit;

namespace Atomix.Client.Core.Tests
{
    public class BsonSerializersTests
    {
        //private class TimeObject
        //{
        //    public DateTime DateTime { get; set; }
        //}

        //[Fact]
        //public void LocalDateTimeSerializeTest()
        //{
        //    var bsonMapper = new BsonMapper();
        //    bsonMapper.UseSerializer(new DateTimeToBsonSerializer());

        //    var time = DateTime.Now.ToUniversalTime();
        //    var timeObject = new TimeObject { DateTime = time };

        //    var document = bsonMapper.ToDocument(timeObject);

        //    Assert.NotNull(document);

        //    var deserializedObj = bsonMapper.ToObject<TimeObject>(document);

        //    Assert.NotEqual(timeObject.DateTime, deserializedObj.DateTime);
        //}
    }
}