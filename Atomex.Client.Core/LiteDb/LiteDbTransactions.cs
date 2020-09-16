using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using LiteDB;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Wallets.Abstract;

namespace Atomex.LiteDb
{
    public partial class LiteDbAccountDataRepository : IAccountDataRepository
    {
        private const string TransactionsCollection = "Transactions";

        private const string TX_CurrencyKey = nameof(IBlockchainTransaction.Currency);

        private byte[] GetTxAssociatedData(BsonDocument document)
        {
            var currency = document[TX_CurrencyKey].AsString;

            return Encoding.UTF8.GetBytes($"{currency}");
        }

        private void CopyTxAssociatedData(BsonDocument from, BsonDocument to)
        {
            to[TX_CurrencyKey] = from[TX_CurrencyKey].AsString;
        }

        public static string UniqueTxId(string currency, string txId) =>
            $"{currency}:{txId}";

        private BsonDocument EncryptTx<T>(
            UnmanagedBytes key,
            T transaction)
        {
            var document = _bsonMapper.ToDocument(transaction);

            var encryptedDocument = LiteDbAeadEncryption.EncryptDocument(
                key: key,
                document: document,
                associatedData: GetTxAssociatedData(document));

            CopyTxAssociatedData(document, encryptedDocument);

            return encryptedDocument;
        }

        public Task<bool> UpsertTransactionAsync<T>(
            T tx,
            CancellationToken cancellationToken = default)
            where T : IBlockchainTransaction
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var encryptedDocument = EncryptTx(key, tx);

                return Task.FromResult(db
                    .GetCollection(TransactionsCollection)
                    .Upsert(encryptedDocument));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbUpsertItem, e, "Error upserting transaction");
            }

            return Task.FromResult(false);
        }

        public Task<int> UpsertTransactionsAsync<T>(
            IEnumerable<T> txs,
            CancellationToken cancellationToken = default)
            where T : IBlockchainTransaction
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var encryptedDocuments = txs
                    .Select(tx => EncryptTx(key, tx))
                    .ToList();

                return Task.FromResult(db
                    .GetCollection(TransactionsCollection)
                    .Upsert(encryptedDocuments));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbUpsertItem, e, "Error upserting transactions");
            }

            return Task.FromResult(0);
        }

        public Task<T> GetTransactionByIdAsync<T>(
            string currency,
            string txId,
            CancellationToken cancellationToken = default)
            where T : IBlockchainTransaction
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var shadowedId = LiteDbAeadEncryption
                    .GetShadowedIdHex(key, UniqueTxId(currency, txId));

                var encryptedDocument = db
                    .GetCollection(TransactionsCollection)
                    .FindById(shadowedId);

                if (encryptedDocument == null)
                    return Task.FromResult<T>(default);

                var document = LiteDbAeadEncryption.DecryptDocument(
                    key: key,
                    document: encryptedDocument,
                    associatedData: GetTxAssociatedData(encryptedDocument));

                return Task.FromResult(_bsonMapper.ToObject<T>(document));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error getting tx");
            }

            return Task.FromResult<T>(default);
        }

        public Task<IEnumerable<T>> GetTransactionsAsync<T>(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
            where T : IBlockchainTransaction
        {
            try
            {
                return Task.FromResult(QueryEntities<T>(
                    collection: TransactionsCollection,
                    predicate: Query.EQ(WA_CurrencyKey, currency),
                    associatedDataCreator: GetTxAssociatedData,
                    offset: offset,
                    limit: limit));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error gettings txs");
            }

            return Task.FromResult(Enumerable.Empty<T>());
        }

        public Task<IEnumerable<T>> GetUnconfirmedTransactionsAsync<T>(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
            where T : IBlockchainTransaction
        {
            try
            {
                var txs = QueryEntities<T>(
                        collection: TransactionsCollection,
                        predicate: Query.EQ(WA_CurrencyKey, currency),
                        associatedDataCreator: GetTxAssociatedData,
                        offset: offset,
                        limit: limit)
                    .Where(t => t.State == BlockchainTransactionState.Pending)
                    .ToList();

                return Task.FromResult((IEnumerable<T>)txs);
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error gettings txs");
            }

            return Task.FromResult(Enumerable.Empty<T>());
        }

        public Task<bool> RemoveTransactionByIdAsync(
            string currency,
            string txId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var shadowedId = LiteDbAeadEncryption
                    .GetShadowedIdHex(key, UniqueTxId(currency, txId));

                return Task.FromResult(db
                    .GetCollection(TransactionsCollection)
                    .Delete(shadowedId));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbRemoveItem, e, "Error removing transaction");
            }

            return Task.FromResult(false);
        }
    }
}