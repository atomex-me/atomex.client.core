using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using LiteDB;

using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Core;
using Atomex.Wallets.Abstract;

namespace Atomex.LiteDb
{
    public partial class LiteDbAccountDataRepository : IAccountDataRepository
    {
        private const string AddressesCollection = "Addresses";

        private const string WA_CurrencyKey = nameof(WalletAddress.Currency);
        private const string WA_HasActivityKey = nameof(WalletAddress.HasActivity);
        private const string WA_KeyIndexKey = nameof(WalletAddress.KeyIndex.Index);
        private const string WA_KeyChainKey = nameof(WalletAddress.KeyIndex.Chain);

        private byte[] GetAddressAssociatedData(BsonDocument document)
        {
            var currency = document[WA_CurrencyKey].AsString;
            var hasActivity = document[WA_HasActivityKey].AsBoolean;

            return Encoding.UTF8.GetBytes($"{currency}:{hasActivity}");
        }

        private void CopyAddressAssociatedData(BsonDocument from, BsonDocument to)
        {
            to[WA_CurrencyKey] = from[WA_CurrencyKey].AsString;
            to[WA_HasActivityKey] = from[WA_HasActivityKey].AsBoolean;
        }

        private BsonDocument EncryptAddress(
            UnmanagedBytes key,
            WalletAddress walletAddress)
        {
            var document = _bsonMapper.ToDocument(walletAddress);

            var encryptedDocument = LiteDbAeadEncryption.EncryptDocument(
                key: key,
                document: document,
                associatedData: GetAddressAssociatedData(document));

            CopyAddressAssociatedData(document, encryptedDocument);

            return encryptedDocument;
        }

        public Task<bool> UpsertAddressAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var encryptedDocument = EncryptAddress(key, walletAddress);

                return Task.FromResult(db
                    .GetCollection(AddressesCollection)
                    .Upsert(encryptedDocument));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbUpsertItem, e, "Error upserting address");
            }

            return Task.FromResult(false);
        }

        public Task<int> UpsertAddressesAsync(
            IEnumerable<WalletAddress> walletAddresses,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var encryptedDocuments = walletAddresses
                    .Select(w => EncryptAddress(key, w))
                    .ToList();

                return Task.FromResult(db
                    .GetCollection(AddressesCollection)
                    .Upsert(encryptedDocuments));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbUpsertItem, e, "Error upserting addresses");
            }

            return Task.FromResult(0);
        }

        public Task<bool> TryInsertAddressAsync(
            WalletAddress walletAddress,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var shadowedId = LiteDbAeadEncryption
                    .GetShadowedIdHex(key, walletAddress.Id);

                var addresses = db.GetCollection(AddressesCollection);

                if (addresses.FindById(shadowedId) != null)
                    return Task.FromResult(false);

                var encryptedDocument = EncryptAddress(key, walletAddress);

                var id = addresses.Insert(encryptedDocument);

                return Task.FromResult(id != null);
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbInsertItem, e, "Error inserting address");
            }

            return Task.FromResult(false);
        }

        public Task<WalletAddress> GetWalletAddressAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var shadowedId = LiteDbAeadEncryption
                    .GetShadowedIdHex(key, WalletAddress.UniqueId(currency, address));

                var encryptedDocument = db
                    .GetCollection(AddressesCollection)
                    .FindById(shadowedId);

                if (encryptedDocument == null)
                    return Task.FromResult<WalletAddress>(null);

                var document = LiteDbAeadEncryption.DecryptDocument(
                    key: key,
                    document: encryptedDocument,
                    associatedData: GetAddressAssociatedData(encryptedDocument));

                return Task.FromResult(_bsonMapper.ToObject<WalletAddress>(document));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error getting address");
            }

            return Task.FromResult<WalletAddress>(null);
        }

        public Task<WalletAddress> GetLastActiveWalletAddressAsync(
            string currency,
            int chain,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var db = new LiteDatabase(ConnectionString, _bsonMapper);
                using var key = _keyDb.ToUnmanagedBytes();

                var encryptedDocuments = db
                    .GetCollection(AddressesCollection)
                    .Find(Query.And(
                        Query.EQ(WA_CurrencyKey, currency),
                        Query.EQ(WA_HasActivityKey, true)));

                var documents = encryptedDocuments
                    .Select(encryptedDocument =>
                    {
                        return LiteDbAeadEncryption.DecryptDocument(
                            key: key,
                            document: encryptedDocument,
                            associatedData: GetAddressAssociatedData(encryptedDocument));
                    })
                    .ToList();

                var maxIndex = 0;
                BsonDocument documentWithMaxIndex = null;

                foreach (var document in documents)
                {
                    var keyIndex = document[WA_KeyIndexKey].AsInt32;
                    var keyChain = document[WA_KeyChainKey].AsInt32;

                    if (keyChain == chain && keyIndex >= maxIndex)
                    {
                        documentWithMaxIndex = document;
                        maxIndex = keyIndex;
                    }
                }

                var walletAddress = documentWithMaxIndex != null
                    ? _bsonMapper.ToObject<WalletAddress>(documentWithMaxIndex)
                    : null;

                return Task.FromResult(walletAddress);
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error getting last active address");
            }

            return Task.FromResult<WalletAddress>(null);
        }

        public Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            string currency,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return Task.FromResult(QueryEntities<WalletAddress>(
                    collection: AddressesCollection,
                    predicate: Query.EQ(WA_CurrencyKey, currency),
                    associatedDataCreator: GetAddressAssociatedData,
                    offset: offset,
                    limit: limit));
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error gettings addresses");
            }

            return Task.FromResult(Enumerable.Empty<WalletAddress>());
        }

        public Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            string currency,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var addresses = QueryEntities<WalletAddress>(
                        collection: AddressesCollection,
                        predicate: Query.And(
                            Query.EQ(WA_CurrencyKey, currency),
                            Query.EQ(WA_HasActivityKey, true)),
                        associatedDataCreator: GetAddressAssociatedData)
                    .Where(a => a.Balance > 0 || a.UnconfirmedIncome > 0)
                    .ToList();

                return Task.FromResult((IEnumerable<WalletAddress>)addresses);
            }
            catch (Exception e)
            {
                _logger?.LogError(LogEvents.LiteDbGetItem, e, "Error getting unspent addresses");
            }

            return Task.FromResult(Enumerable.Empty<WalletAddress>());
        }
    }
}