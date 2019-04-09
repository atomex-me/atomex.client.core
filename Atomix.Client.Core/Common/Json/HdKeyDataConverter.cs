using System;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.BitcoinBased;
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
                : nameof(BitcoinBasedHdKeyData);

            if (type.Equals(nameof(BitcoinBasedHdKeyData)))
                return new BitcoinBasedHdKeyData(keyData);

            if (type.Equals(nameof(EthereumHdKeyData)))
                return new EthereumHdKeyData(keyData);

            if (type.Equals(nameof(TezosHdKeyData)))
                return new TezosHdKeyData(keyData);

            throw new NotSupportedException("Key data type not supported");
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(IHdKeyData).IsAssignableFrom(objectType);
        }
    }
}