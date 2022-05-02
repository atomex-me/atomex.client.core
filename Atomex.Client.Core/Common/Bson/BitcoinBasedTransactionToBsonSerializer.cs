﻿using System;
using System.Collections.Generic;
using System.Linq;

using LiteDB;
using NBitcoin;

using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Core;

namespace Atomex.Common.Bson
{
    public class BitcoinBasedTransactionToBsonSerializer : BsonSerializer<BitcoinBasedTransaction>
    {
        private const string CreationTimeKey = nameof(IBlockchainTransaction_OLD.CreationTime);
        private const string CurrencyKey     = nameof(IBlockchainTransaction_OLD.Currency);
        private const string TxKey           = "Tx";
        private const string TxIdKey         = "TxId";
        private const string BlockInfoKey    = nameof(IBlockchainTransaction_OLD.BlockInfo);
        private const string StateKey        = nameof(IBlockchainTransaction_OLD.State);
        private const string TypeKey         = nameof(IBlockchainTransaction_OLD.Type);
        private const string FeesKey         = nameof(BitcoinBasedTransaction.Fees);
        private const string AmountKey       = nameof(BitcoinBasedTransaction.Amount);

        private readonly IEnumerable<CurrencyConfig> _currencies;

        public BitcoinBasedTransactionToBsonSerializer(IEnumerable<CurrencyConfig> currencies)
        {
            _currencies = currencies;
        }

        public override BitcoinBasedTransaction Deserialize(BsonValue tx)
        {
            var bson = tx as BsonDocument;
            if (bson == null)
                return null;

            var currencyName = bson[CurrencyKey].IsString
                ? bson[CurrencyKey].AsString
                : string.Empty;

            var currency = _currencies.FirstOrDefault(c => c.Name.Equals(currencyName));

            if (currency is BitcoinBasedConfig btcBaseCurrency)
            {
                var blockInfo = !bson[BlockInfoKey].IsNull
                    ? BsonMapper.ToObject<BlockInfo>(bson[BlockInfoKey].AsDocument)
                    : null;

                return new BitcoinBasedTransaction(
                    currency: btcBaseCurrency.Name,
                    tx: Transaction.Parse(bson[TxKey].AsString, btcBaseCurrency.Network),
                    blockInfo: blockInfo,
                    fees: !bson[FeesKey].IsNull
                        ? (long?)bson[FeesKey].AsInt64
                        : null
                )
                {
                    State = !bson[StateKey].IsNull
                        ? (BlockchainTransactionState)Enum.Parse(typeof(BlockchainTransactionState), bson[StateKey].AsString)
                        : BlockchainTransactionState.Unknown,

                    Type = !bson[TypeKey].IsNull
                        ? (BlockchainTransactionType)Enum.Parse(typeof(BlockchainTransactionType), bson[TypeKey].AsString)
                        : BlockchainTransactionType.Unknown,

                    Amount = !bson[AmountKey].IsNull
                        ? bson[AmountKey].AsInt64
                        : 0,

                    CreationTime = bson.ContainsKey(CreationTimeKey) && !bson[CreationTimeKey].IsNull
                        ? bson[CreationTimeKey].AsDateTime
                        : blockInfo?.FirstSeen ?? blockInfo?.BlockTime
                };
            }

            return null;
        }

        public override BsonValue Serialize(BitcoinBasedTransaction tx)
        {
            if (tx == null)
                return null;

            return new BsonDocument
            {
                [IdKey]           = tx.UniqueId,
                [TxIdKey]         = tx.Id,
                [CreationTimeKey] = tx.CreationTime,
                [CurrencyKey]     = tx.Currency,
                [TxKey]           = tx.ToBytes().ToHexString(),
                [BlockInfoKey]    = tx.BlockInfo != null
                    ? BsonMapper.ToDocument(tx.BlockInfo)
                    : null,
                [FeesKey]         = tx.Fees,
                [StateKey]        = tx.State.ToString(),
                [TypeKey]         = tx.Type.ToString(),
                [AmountKey]       = tx.Amount
            };
        }
    }
}