using System;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Atomix.Common.Json
{
    //public class TxOutputConverter : JsonConverter
    //{
    //    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    //    {
    //        if (value is BitcoinBaseTxOutput output)
    //        {
    //            var o = new JObject
    //            {
    //                ["type"] = nameof(BitcoinBaseCurrency),
    //                ["txid"] = output.TxId,
    //                ["index"] = output.Index,
    //                ["value"] = output.Value,
    //                ["spent_index"] = output.SpentTxPoint?.Index,
    //                ["spent_hash"] = output.SpentTxPoint?.Hash,
    //                ["script"] = output.Coin.TxOut.ScriptPubKey.ToHex()
    //                // witness script
    //            };
    //            o.WriteTo(writer);
    //            return;
    //        }

    //        throw new JsonException($"Invalid json format. Unsupported txoutput type {value?.GetType()}");
    //    }

    //    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    //    {
    //        var jsonObject = JObject.Load(reader);

    //        if (jsonObject.TryGetValue("type", out var token))
    //        {
    //            if (token.Value<string>().Equals(nameof(BitcoinBaseCurrency)))
    //            {
    //                var spentHash = jsonObject["spent_hash"].Value<string>();
    //                var spentPoint = !string.IsNullOrEmpty(spentHash)
    //                    ? new TxPoint(jsonObject["spent_index"].Value<uint>(), spentHash)
    //                    : null; 

    //                return new BitcoinBaseTxOutput(
    //                    coin: new Coin(
    //                        fromTxHash: new uint256(jsonObject["txid"].Value<string>()),
    //                        fromOutputIndex: jsonObject["index"].Value<uint>(),
    //                        amount: new Money(jsonObject["value"].Value<long>()),
    //                        scriptPubKey: new Script(Hex.FromString(jsonObject["script"].Value<string>()))
    //                    ),
    //                    spentTxPoint: spentPoint
    //                    // witness script
    //                );
    //            }

    //            throw new JsonException($"Invalid json format. Unsupported txoutput type {token.Value<string>()}");
    //        }

    //        throw new JsonException("Invalid json format. Missing field 'type'");
    //    }

    //    public override bool CanConvert(Type objectType)
    //    {
    //        return typeof(ITxOutput).IsAssignableFrom(objectType);
    //    }
    //}
}