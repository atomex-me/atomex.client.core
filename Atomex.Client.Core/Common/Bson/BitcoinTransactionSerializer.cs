using System.Numerics;

using LiteDB;
using NBitcoin;

using Atomex.Blockchain.Bitcoin;
using Network = Atomex.Core.Network;
using System;

namespace Atomex.Common.Bson
{
    public class BitcoinTransactionSerializer : BsonSerializer<BitcoinTransaction>
    {
        private readonly Network _network;

        public BitcoinTransactionSerializer(Network network)
        {
            _network = network;
        }

        public override BitcoinTransaction Deserialize(BsonValue bsonValue)
        {
            return new BitcoinTransaction(
                currency: bsonValue["Currency"].AsString,
                tx: Transaction.Parse(bsonValue["Hex"].AsString, BitcoinNetworkResolver.ResolveNetwork(bsonValue["Currency"].AsString, _network)),
                creationTime: !bsonValue["CreationTime"].IsNull ? bsonValue["CreationTime"].AsDateTime : DateTimeOffset.MinValue,
                blockTime: !bsonValue["BlockTime"].IsNull ? bsonValue["BlockTime"].AsDateTime : DateTimeOffset.MinValue,
                blockHeight: bsonValue["BlockHeight"].AsInt64,
                confirmations: bsonValue["Confirmations"].AsInt64,
                fee: BsonMapper.Deserialize<BigInteger>(bsonValue["Fee"]));
        }

        public override BsonValue Serialize(BitcoinTransaction obj)
        {
            return new BsonDocument
            {
                ["_id"]           = obj.Id,
                ["Currency"]      = obj.Currency,
                ["Status"]        = obj.Status.ToString(),
                ["CreationTime"]  = obj.CreationTime != null ? obj.CreationTime.Value.DateTime : BsonValue.Null,
                ["BlockTime"]     = obj.BlockTime != null ? obj.BlockTime.Value.DateTime : BsonValue.Null,
                ["BlockHeight"]   = obj.BlockHeight,
                ["Confirmations"] = obj.Confirmations,
                ["Fee"]           = BsonMapper.Serialize(obj.ResolvedFee),
                ["Hex"]           = obj.ToHex()
            };
        }
    }
}