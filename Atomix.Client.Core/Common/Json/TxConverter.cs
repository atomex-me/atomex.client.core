using System;
using System.Collections.Generic;
using System.Linq;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Core.Entities;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Atomix.Common.Json
{
    //public class TxConverter : JsonConverter
    //{
    //    private IEnumerable<Currency> Currencies { get; }

    //    public TxConverter()
    //        : this(null)
    //    {
    //    }

    //    public TxConverter(IEnumerable<Currency> currencies)
    //    {
    //        Currencies = currencies;
    //    }

    //    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    //    {
    //        if (value is BitcoinBaseTransaction tx)
    //        {
    //            var jObject = new JObject
    //            {
    //                ["currency"] = tx.Currency.Name,
    //                ["tx"] = tx.ToBytes().ToHexString(),
    //                ["fees"] = tx.Fees,
    //                ["confirmations"] = tx.Confirmations,
    //                ["blockheight"] = tx.BlockHeight,
    //                ["firstseen"] = tx.FirstSeen,
    //                ["blocktime"] = tx.BlockTime
    //            };
                
    //            writer.WriteToken(jObject.CreateReader());
    //            return;
    //        }

    //        throw new JsonException($"Invalid json format. Unsupported tx type {value?.GetType()}");
    //    }

    //    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    //    {
    //        var token = JToken.Load(reader);

    //        if (token.Type == JTokenType.Null)
    //            return null;

    //        if (token.Type != JTokenType.Object)
    //            throw new InvalidOperationException("Invalid json");

    //        var @object = (JObject)token;

    //        var currencyName = @object["currency"].Value<string>();
    //        var currency = Currencies.FirstOrDefault(c => c.Name.Equals(currencyName));
         
    //        if (currency is BitcoinBaseCurrency btcBaseCurrency)
    //        {
    //            return new BitcoinBaseTransaction(
    //                currency: btcBaseCurrency,
    //                tx: Transaction.Parse(@object["tx"].Value<string>(), btcBaseCurrency.Network),
    //                fees: @object["fees"].Value<long>(),
    //                confirmations: @object["confirmations"].Value<int>(),
    //                blockHeight: @object["blockheight"].Value<int>(),
    //                firstSeen: @object["firstseen"].Value<DateTime>(),
    //                blockTime: @object["blocktime"].Value<DateTime>());
    //        }

    //        throw new JsonException("Invalid json format. Unsupported tx type.");
    //    }

    //    public override bool CanConvert(Type objectType)
    //    {
    //        return typeof(IBlockchainTransaction).IsAssignableFrom(objectType);
    //    }
    //}
}