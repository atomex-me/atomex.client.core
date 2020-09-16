using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using LiteDB;

using Atomex.Common;
using Atomex.Core;
using Atomex.Wallets.Abstract;

namespace Atomex.LiteDb
{
    public partial class LiteDbAccountDataRepository : IAccountDataRepository
    {
        private const string SwapsCollection = "Swaps";

        public Task<bool> InsertSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var document = _bsonMapper.ToDocument(swap);

                var encryptedDocument = LiteDbAeadEncryption.EncryptDocument(
                    key: key,
                    document: document,
                    associatedData: new byte[0]);

                var id = db
                    .GetCollection(SwapsCollection)
                    .Insert(encryptedDocument);

                return Task.FromResult(id != null);
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbInsertItem, e, "Error inserting swap");
            }

            return Task.FromResult(false);
        }

        public Task<bool> UpdateSwapAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var document = _bsonMapper.ToDocument(swap);

                var encryptedDocument = LiteDbAeadEncryption.EncryptDocument(
                    key: key,
                    document: document,
                    associatedData: new byte[0]);

                return Task.FromResult(db
                    .GetCollection(SwapsCollection)
                    .Update(encryptedDocument));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbInsertItem, e, "Error updating swap");
            }

            return Task.FromResult(false);
        }

        public Task<Swap> GetSwapByIdAsync(
            long id,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var shadowedId = LiteDbAeadEncryption
                    .GetShadowedIdHex(key, id.ToString());

                var encryptedDocument = db
                    .GetCollection(SwapsCollection)
                    .FindById(shadowedId);

                if (encryptedDocument == null)
                    return Task.FromResult<Swap>(null);

                var document = LiteDbAeadEncryption.DecryptDocument(
                    key: key,
                    document: encryptedDocument,
                    associatedData: new byte[0]);

                return Task.FromResult(_bsonMapper.ToObject<Swap>(document));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error getting swap");
            }

            return Task.FromResult<Swap>(null);
        }

        public Task<IEnumerable<Swap>> GetSwapsAsync(
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var swaps = QueryEntities<Swap>(
                    collection: SwapsCollection,
                    predicate: null,
                    offset: offset,
                    limit: limit);

                return Task.FromResult(swaps);
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error getting swaps");
            }

            return Task.FromResult(Enumerable.Empty<Swap>());
        }
    }
}