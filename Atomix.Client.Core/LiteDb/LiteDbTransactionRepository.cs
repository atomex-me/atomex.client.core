using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Common.Bson;
using Atomix.Core.Entities;
using LiteDB;
using Serilog;

namespace Atomix.LiteDb
{
    public class LiteDbTransactionRepository : LiteDbRepository, ITransactionRepository
    {
        //public const string IdKey = "_id";
        public const string CurrencyKey = "currency";
        public const string AddressKey = "address";
        public const string TransactionCollectionName = "transactions";
        public const string OutputsCollectionName = "outputs";

        public LiteDbTransactionRepository(string pathToDb, SecureString password)
            : base(pathToDb, password)
        {
            //Debug();
        }

        //private void Debug()
        //{
        //    using (var db = new LiteDatabase(ConnectionString))
        //    {
        //        var transactions = db.GetCollection(TransactionCollectionName);

        //        transactions.Delete(Query.EQ(CurrencyKey, "ETH"));
        //    }
        //}

        public Task<bool> AddTransactionAsync(IBlockchainTransaction tx)
        {
            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var transactions = db.GetCollection(TransactionCollectionName);

                    var document = BsonMapper.Global.ToDocument(tx);

                    transactions.EnsureIndex(CurrencyKey);
                    transactions.Upsert(document);
                }

                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding transaction");
            }

            return Task.FromResult(false);
        }

        public Task<bool> AddOutputsAsync(IEnumerable<ITxOutput> outputs, Currency currency, string address)
        {
            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var outputsCollection = db.GetCollection(OutputsCollectionName);

                    var documents = outputs
                        .Select(o =>
                        {
                            var document = BsonMapper.Global.ToDocument(o);
                            document[CurrencyKey] = currency.Name;
                            document[AddressKey] = address;
                            return document;
                        });

                    outputsCollection.EnsureIndex(CurrencyKey);
                    outputsCollection.Upsert(documents);
                }

                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding transaction");
            }

            return Task.FromResult(false);
        }

        public Task<IBlockchainTransaction> GetTransactionByIdAsync(Currency currency, string txId)
        {
            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var transactions = db.GetCollection(TransactionCollectionName);

                    var document = transactions.FindById(txId);

                    if (document != null)
                    {
                        var tx = (IBlockchainTransaction) BsonMapper.Global.ToObject(
                            type: currency.TransactionType,
                            doc: document);

                        return Task.FromResult(tx);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transaction by id");
            }

            return Task.FromResult<IBlockchainTransaction>(null);
        }

        public Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(Currency currency)
        {
            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var transactionsCollection = db.GetCollection(TransactionCollectionName);

                    var documents = transactionsCollection
                        .Find(d => d[CurrencyKey] == currency.Name);

                    var transactions = documents.Select(d => (IBlockchainTransaction) BsonMapper.Global.ToObject(
                        type: currency.TransactionType,
                        doc: d));

                    return Task.FromResult(transactions);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting transactions");
            }

            return Task.FromResult(Enumerable.Empty<IBlockchainTransaction>());
        }

        public async Task<IEnumerable<IBlockchainTransaction>> GetUnconfirmedTransactionsAsync(Currency currency)
        {
            var transactions = await GetTransactionsAsync(currency)
                .ConfigureAwait(false);

            return transactions.Where(t => !t.IsConfirmed());
        }

        public async Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(Currency currency, bool skipUnconfirmed = true)
        {
            var outputs = await GetOutputsAsync(currency)
                .ConfigureAwait(false);

            if (!skipUnconfirmed)
                return outputs.Where(o => !o.IsSpent);

            return await GetUnspentConfirmedOutputsAsync(currency, outputs)
                .ConfigureAwait(false);
        }

        public async Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(Currency currency, string address, bool skipUnconfirmed = true)
        {
            var outputs = await GetOutputsAsync(currency, address)
                .ConfigureAwait(false);

            if (!skipUnconfirmed)
                return outputs.Where(o => !o.IsSpent);

            return await GetUnspentConfirmedOutputsAsync(currency, outputs)
                .ConfigureAwait(false);
        }

        private async Task<IEnumerable<ITxOutput>> GetUnspentConfirmedOutputsAsync(
            Currency currency,
            IEnumerable<ITxOutput> outputs)
        {
            var unconfirmedTransactions = await GetUnconfirmedTransactionsAsync(currency)
                .ConfigureAwait(false);

            return outputs
                .Where(o => !o.IsSpent)
                .Where(o => unconfirmedTransactions
                    .Cast<IInOutTransaction>()
                    .FirstOrDefault(t => t.Inputs
                        .FirstOrDefault(i => i.Hash.Equals(o.TxId) && i.Index.Equals(o.Index)) != null) == null);
        }

        public Task<IEnumerable<ITxOutput>> GetOutputsAsync(Currency currency)
        {
            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var outputsCollection = db.GetCollection(OutputsCollectionName);

                    var documents = outputsCollection
                        .Find(d => d[CurrencyKey] == currency.Name);

                    var outputs = documents.Select(d => (ITxOutput) BsonMapper.Global.ToObject(
                        type: d.OutputType(),
                        doc: d));

                    return Task.FromResult(outputs);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting outputs");
            }

            return Task.FromResult(Enumerable.Empty<ITxOutput>());
        }

        public Task<IEnumerable<ITxOutput>> GetOutputsAsync(Currency currency, string address)
        {
            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var outputsCollection = db.GetCollection(OutputsCollectionName);

                    var documents = outputsCollection
                        .Find(d => d[CurrencyKey] == currency.Name && d[AddressKey] == address);

                    var outputs = documents.Select(d => (ITxOutput) BsonMapper.Global.ToObject(
                        type: d.OutputType(),
                        doc: d));

                    return Task.FromResult(outputs);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting outputs");
            }

            return Task.FromResult(Enumerable.Empty<ITxOutput>());
        }
    }
}