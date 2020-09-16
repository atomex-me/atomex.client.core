using System;
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
        private const string OrdersCollection = "Orders";

        public Task<bool> UpsertOrderAsync(
            Order order,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var document = _bsonMapper.ToDocument(order);

                var encryptedDocument = LiteDbAeadEncryption.EncryptDocument(
                    key: key,
                    document: document,
                    associatedData: new byte[0]);

                return Task.FromResult(db
                    .GetCollection(OrdersCollection)
                    .Upsert(encryptedDocument));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbUpsertItem, e, "Error upserting order");
            }

            return Task.FromResult(false);
        }

        public Task<Order> GetOrderByIdAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var shadowedId = LiteDbAeadEncryption
                    .GetShadowedIdHex(key, clientOrderId);

                var encryptedDocument = db
                    .GetCollection(OrdersCollection)
                    .FindById(shadowedId);

                if (encryptedDocument == null)
                    return Task.FromResult<Order>(null);

                var document = LiteDbAeadEncryption.DecryptDocument(
                    key: key,
                    document: encryptedDocument,
                    associatedData: new byte[0]);

                return Task.FromResult(_bsonMapper.ToObject<Order>(document));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error getting order");
            }

            return Task.FromResult<Order>(null);
        }

        public Task<Order> GetOrderByIdAsync(
            long id,
            CancellationToken cancellationToken = default)
        {
            return GetOrderByIdAsync(id.ToString(), cancellationToken);
        }
    }
}