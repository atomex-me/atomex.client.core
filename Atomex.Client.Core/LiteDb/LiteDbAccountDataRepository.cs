using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using LiteDB;

using Atomex.Abstract;
using Atomex.Common.Bson;
using Atomex.Common.Memory;
using Atomex.Cryptography.Abstract;
using Atomex.Wallets.Abstract;

namespace Atomex.LiteDb
{
    public partial class LiteDbAccountDataRepository : IAccountDataRepository
    {
        private readonly string _pathToDb;
        private readonly SecureBytes _keyDb;
        private readonly ILogger<LiteDbAccountDataRepository> _logger;
        private readonly BsonMapper _bsonMapper;

        private string ConnectionString => $"FileName={_pathToDb}";

        public LiteDbAccountDataRepository(
            string pathToDb,
            SecureBytes keyPassword,
            ICurrencies currencies,
            ILogger<LiteDbAccountDataRepository> logger = null)
        {
            _pathToDb = pathToDb ?? throw new ArgumentNullException(nameof(pathToDb));
            _logger = logger;

            using var unmanagedKeyPassword = keyPassword.ToUnmanagedBytes();
            using var unmanagedKeyDb = new UnmanagedBytes(32);

            MacAlgorithm.HmacSha256.Mac(
                key: unmanagedKeyPassword,
                data: Encoding.UTF8.GetBytes("db_encryption"),
                mac: unmanagedKeyDb);

            _keyDb = new SecureBytes(unmanagedKeyDb);
            _bsonMapper = CreateBsonMapper(currencies);
        }

        private BsonMapper CreateBsonMapper(ICurrencies currencies)
        {
            return new BsonMapper()
                .UseSerializer(new CurrencyToBsonSerializer(currencies))
                .UseSerializer(new BigIntegerToBsonSerializer())
                .UseSerializer(new JObjectToBsonSerializer())
                .UseSerializer(new WalletAddressToBsonSerializer())
                .UseSerializer(new OrderToBsonSerializer())
                .UseSerializer(new BitcoinBasedTransactionToBsonSerializer(currencies))
                .UseSerializer(new BitcoinBasedTxOutputToBsonSerializer())
                .UseSerializer(new EthereumTransactionToBsonSerializer())
                .UseSerializer(new TezosTransactionToBsonSerializer())
                .UseSerializer(new SwapToBsonSerializer(currencies));
        }

        private IEnumerable<T> QueryEntities<T>(
            string collection,
            BsonExpression predicate = null,
            Func<BsonDocument, byte[]> associatedDataCreator = null,
            int offset = 0,
            int limit = int.MaxValue)
        {
            using var db = new LiteDatabase(ConnectionString, _bsonMapper);
            using var key = _keyDb.ToUnmanagedBytes();

            var encryptedDocuments = predicate != null
                ? db.GetCollection(collection).Find(predicate, offset, limit)
                : db.GetCollection(collection).FindAll();

            var entities = encryptedDocuments
                .Select(encryptedDocument =>
                {
                    var document = LiteDbAeadEncryption.DecryptDocument(
                        key: key,
                        document: encryptedDocument,
                        associatedData: associatedDataCreator?.Invoke(encryptedDocument) ?? new byte[0]);

                    return _bsonMapper.ToObject<T>(document);
                })
                .ToList();

            return entities;
        }
    }
}