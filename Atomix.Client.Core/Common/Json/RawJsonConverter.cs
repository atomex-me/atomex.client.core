//using System;
//using Newtonsoft.Json;

//namespace Atomix.Common.Json
//{
//    public class RawJsonConverter : JsonConverter
//    {
//        public override bool CanConvert(Type objectType)
//        {
//            return objectType == typeof(string);
//        }

//        public override bool CanRead => false;

//        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
//        {
//            throw new NotImplementedException();
//        }

//        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
//        {
//            writer.WriteRawValue((string)value);
//        }
//    }
//}