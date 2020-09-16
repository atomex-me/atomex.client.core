using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using LiteDB;

using Atomex.Common;
using Atomex.Wallets.Abstract;
using Atomex.Blockchain.Abstract;

namespace Atomex.LiteDb
{
    public partial class LiteDbAccountDataRepository : IAccountDataRepository
    {
        private const string OutputsCollection = "Outputs";

        private const string OT_CurrencyKey = "Currency";
        private const string OT_AddressKey = "Address";

        public Task<bool> UpsertOutputAsync(
            ITxOutput output,
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var document = _bsonMapper.ToDocument(output);
                document[OT_CurrencyKey] = currency;
                document[OT_AddressKey] = address;

                var encryptedDocument = LiteDbAeadEncryption.EncryptDocument(
                    key: key,
                    document: document,
                    associatedData: new byte[0]);

                return Task.FromResult(db
                    .GetCollection(OutputsCollection)
                    .Upsert(encryptedDocument));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbUpsertItem, e, "Error upserting output");
            }

            return Task.FromResult(false);
        }

        public Task<int> UpsertOutputsAsync(
            IEnumerable<ITxOutput> outputs,
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var encryptedDocuments = outputs.Select(output =>
                {
                    var document = _bsonMapper.ToDocument(output);
                    document[OT_CurrencyKey] = currency;
                    document[OT_AddressKey] = address;

                    var encryptedDocument = LiteDbAeadEncryption.EncryptDocument(
                        key: key,
                        document: document,
                        associatedData: new byte[0]);

                    return encryptedDocument;
                });

                return Task.FromResult(db
                    .GetCollection(OutputsCollection)
                    .Upsert(encryptedDocuments));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbUpsertItem, e, "Error upserting outputs");
            }

            return Task.FromResult(0);
        }

        public async Task<IEnumerable<TOutput>> GetAvailableOutputsAsync<TOutput, TTransaction>(
            string currency,
            CancellationToken cancellationToken = default)
            where TOutput : ITxOutput
            where TTransaction : IBlockchainTransaction
        {
            var outputs = (await GetOutputsAsync<TOutput>(currency)
                .ConfigureAwait(false))
                .Where(o => !o.IsSpent)
                .ToList();

            return await GetOnlyConfirmedOutputsAsync<TOutput, TTransaction>(currency, outputs)
                .ConfigureAwait(false);
        }

        public async Task<IEnumerable<TOutput>> GetAvailableOutputsAsync<TOutput, TTransaction>(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
            where TOutput : ITxOutput
            where TTransaction : IBlockchainTransaction
        {
            var outputs = (await GetOutputsAsync<TOutput>(currency, address)
                .ConfigureAwait(false))
                .Where(o => !o.IsSpent)
                .ToList();

            return await GetOnlyConfirmedOutputsAsync<TOutput, TTransaction>(currency, outputs)
                .ConfigureAwait(false);
        }

        private async Task<IEnumerable<TOutput>> GetOnlyConfirmedOutputsAsync<TOutput, TTransaction>(
            string currency,
            IEnumerable<TOutput> outputs)
            where TOutput : ITxOutput
            where TTransaction : IBlockchainTransaction
        {
            var confirmedOutputs = new List<TOutput>();

            foreach (var o in outputs)
            {
                var tx = await GetTransactionByIdAsync<TTransaction>(currency, o.TxId)
                    .ConfigureAwait(false);

                if (tx?.IsConfirmed ?? false)
                    confirmedOutputs.Add(o);
            }

            return confirmedOutputs;
        }

        public Task<IEnumerable<T>> GetOutputsAsync<T>(
            string currency,
            CancellationToken cancellationToken = default)
            where T : ITxOutput
        {
            try
            {
                var outputs = QueryEntities<T>(
                    collection: OutputsCollection,
                    predicate: Query.EQ(OT_CurrencyKey, currency));

                return Task.FromResult(outputs);
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error getting outputs");
            }

            return Task.FromResult(Enumerable.Empty<T>());
        }

        public Task<IEnumerable<T>> GetOutputsAsync<T>(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
            where T : ITxOutput
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var encryptedDocuments = db
                    .GetCollection(OutputsCollection)
                    .Find(Query.EQ(OT_CurrencyKey, currency));

                var entities = encryptedDocuments
                    .Select(encryptedDocument =>
                    {
                        return LiteDbAeadEncryption.DecryptDocument(
                            key: key,
                            document: encryptedDocument,
                            associatedData: new byte[0]);
                    })
                    .Where(d => d[OT_AddressKey].AsString == address)
                    .Select(d => _bsonMapper.ToObject<T>(d))
                    .ToList();

                return Task.FromResult((IEnumerable<T>)entities);
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error getting outputs");
            }

            return Task.FromResult(Enumerable.Empty<T>());
        }

        public Task<T> GetOutputAsync<T>(
            string currency,
            string txId,
            uint index,
            CancellationToken cancellationToken = default)
            where T : ITxOutput
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var shadowedId = LiteDbAeadEncryption.GetShadowedIdHex(key, $"{txId}:{index}");

                var encryptedDocument = db
                    .GetCollection(OutputsCollection)
                    .FindById(shadowedId);

                var document = LiteDbAeadEncryption.DecryptDocument(
                    key: key,
                    document: encryptedDocument,
                    associatedData: new byte[0]);

                return Task.FromResult(_bsonMapper.ToObject<T>(document));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error getting output");
            }

            return Task.FromResult(default(T));
        }
    }
}