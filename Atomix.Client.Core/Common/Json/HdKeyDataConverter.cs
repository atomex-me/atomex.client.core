using System;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.KeyData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Atomix.Common.Json
{
    public class HdKeyDataConverter : JsonConverter
    {
        public const string TypeKey = "Type";

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var keyData = (IHdKeyData) value;

            keyData.ToJsonObject().WriteTo(writer);       
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);

            if (token.Type != JTokenType.Object)
                throw new InvalidOperationException("Invalid json");

            var keyData = (JObject)token;

            var type = keyData.ContainsKey(TypeKey)
                ? keyData[TypeKey].Value<string>()
                : nameof(BitcoinBaseHdKeyData);

            if (type.Equals(nameof(BitcoinBaseHdKeyData)))
                return new BitcoinBaseHdKeyData(keyData);

            if (type.Equals(nameof(EthereumHdKeyData)))
                return new EthereumHdKeyData(keyData);

            throw new NotSupportedException("Key data type not supported");
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(IHdKeyData).IsAssignableFrom(objectType);
        }
    }
}