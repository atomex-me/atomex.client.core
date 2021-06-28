using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Atomex.Common;
using Atomex.Core;
using LiteDB;
using Serilog;

namespace Atomex.LiteDb
{
    public static class LiteDbMigrations
    {
        public const ushort LastVersion = Version5;

        public const ushort Version0 = 0;
        public const ushort Version1 = 1;
        public const ushort Version2 = 2;
        public const ushort Version3 = 3;
        public const ushort Version4 = 4;
        public const ushort Version5 = 5;
        public const ushort Version6 = 6;

        public static ushort MigrateFrom_0_to_1(string pathToDb, string sessionPassword)
        {
            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive");
            
            if (db.Engine.UserVersion != Version0)
                throw new Exception("Invalid db version");

            Backup(pathToDb);

            // first "raw" version, simply reload all transaction from blockchain
            var removed = db.GetCollection("Transactions").Delete(Query.All());

            Log.Debug($"{removed} transactions removed by migration");

            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version0, toVersion: Version1);

            return Version1;
        }

        public static ushort MigrateFrom_1_to_2(
            string pathToDb,
            string sessionPassword,
            Network network)
        {
            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive");
            
            if (db.Engine.UserVersion != Version1)
                throw new Exception("Invalid db version");

            if (network != Network.TestNet)
            {
                UpdateVersion(db: db, fromVersion: Version1, toVersion: Version2);
                return Version2;
            }

            Backup(pathToDb);

            // remove all tezos transactions
            db.GetCollection("Transactions").Delete(Query.EQ("Currency", "XTZ"));

            // remove all tezos addresses
            db.GetCollection("Addresses").Delete(Query.EQ("Currency", "XTZ"));

            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version1, toVersion: Version2);
            

            return Version2;
        }

        public static ushort MigrateFrom_2_to_3(
            string pathToDb,
            string sessionPassword,
            Network network)
        {
            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive");
            
            if (db.Engine.UserVersion != Version2)
                throw new Exception("Invalid db version");

            Backup(pathToDb);

            // update wallet addresses
            var addressesCollection = db.GetCollection("Addresses");
            var addresses = addressesCollection.FindAll().ToList();

            foreach (var address in addresses)
            {
                address["Address"] = address["_id"].AsString;
                address["_id"] = $"{address["_id"].AsString}:{address["Currency"].AsString}";
            }

            addressesCollection.Delete(Query.All());
            var inserted = addressesCollection.Upsert(addresses);

            // update transactions
            var transactionsCollection = db.GetCollection("Transactions");
            var transactions = transactionsCollection.FindAll().ToList();

            foreach (var transaction in transactions)
            {
                transaction["TxId"] = transaction["_id"].AsString;
                transaction["_id"] = $"{transaction["_id"].AsString}:{transaction["Currency"].AsString}";
            }

            transactionsCollection.Delete(Query.All());
            inserted = transactionsCollection.Upsert(transactions);

            if (network == Network.TestNet)
            {
                db.GetCollection("Transactions").Delete(Query.EQ("Currency", "XTZ"));
                db.GetCollection("Addresses").Delete(Query.EQ("Currency", "XTZ"));
            }

            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version2, toVersion: Version3);

            return Version3;
        }

        public static ushort MigrateFrom_3_to_4(
            string pathToDb,
            string sessionPassword,
            Network network)
        {
            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive");

            if (db.Engine.UserVersion != Version3)
                throw new Exception("Invalid db version");

            Backup(pathToDb);

            var currencies = new []{ "BTC", "LTC", "ETH", "XTZ", "USDT", "FA12" };

            var singleSuffixes = currencies.Select(c => $":{c}");
            var doubleSuffixes = currencies.Select(c => $":{c}:{c}");

            // fix wallet addresses
            var addressesCollection = db.GetCollection("Addresses");
            var addresses = addressesCollection.FindAll().ToList();

            foreach (var addressInBson in addresses)
            {
                var address = addressInBson["Address"].AsString;

                foreach (var suffix in singleSuffixes)
                {
                    if (address.EndsWith(suffix))
                    {
                        address = address.Substring(0, address.Length - suffix.Length);
                        addressInBson["Address"] = address;
                        break;
                    }
                }

                var id = addressInBson["_id"].AsString;

                foreach (var suffix in doubleSuffixes)
                {
                    if (id.EndsWith(suffix))
                    {
                        id = id.Substring(0, id.Length - suffix.Length / 2);
                        addressInBson["_id"] = id;
                        break;
                    }
                }
            }

            addresses = addresses
                .Distinct(new Common.EqualityComparer<BsonDocument>(
                    (x, y) => x["_id"].AsString == y["_id"].AsString,
                    x => x["_id"].AsString.GetHashCode()))
                .ToList();

            addressesCollection.Delete(Query.All());
            var inserted = addressesCollection.Upsert(addresses);

            // fix transactions
            var transactionsCollection = db.GetCollection("Transactions");
            var transactions = transactionsCollection.FindAll().ToList();

            foreach (var transactionInBson in transactions)
            {
                var txId = transactionInBson["TxId"].AsString;

                foreach (var suffix in singleSuffixes)
                {
                    if (txId.EndsWith(suffix))
                    {
                        txId = txId.Substring(0, txId.Length - suffix.Length);
                        transactionInBson["TxId"] = txId;
                        break;
                    }
                }

                var id = transactionInBson["_id"].AsString;

                foreach (var suffix in doubleSuffixes)
                {
                    if (id.EndsWith(suffix))
                    {
                        id = id.Substring(0, id.Length - suffix.Length / 2);
                        transactionInBson["_id"] = id;
                        break;
                    }
                }
            }

            transactions = transactions
                .Distinct(new Common.EqualityComparer<BsonDocument>(
                    (x, y) => x["_id"].AsString == y["_id"].AsString,
                    x => x["_id"].AsString.GetHashCode()))
                .ToList();

            transactionsCollection.Delete(Query.All());
            inserted = transactionsCollection.Upsert(transactions);

            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version3, toVersion: Version4);

            return Version4;
        }

        public static ushort MigrateFrom_4_to_5(
            string pathToDb,
            string sessionPassword,
            Network network)
        {
            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive");

            if (db.Engine.UserVersion != Version4)
                throw new Exception("Invalid db version");

            Backup(pathToDb);

            // fix outputs
            var outputsCollection = db.GetCollection("Outputs");
            var deletedOutputs = outputsCollection.Delete(Query.EQ("Currency", "BTC"));

            // fix transactions
            var transactionsCollection = db.GetCollection("Transactions");
            var transactions = transactionsCollection.Delete(Query.EQ("Currency", "BTC"));

            // fix addresses
            var addressesCollection = db.GetCollection("Addresses");
            var addresses = addressesCollection.Delete(Query.EQ("Currency", "BTC"));

            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version4, toVersion: Version5);

            return Version5;
        }
        
        public static ushort MigrateFrom_5_to_6(string pathToDb, string sessionPassword)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive";
            
            using var db_ver = new LiteDatabase(connectionString);
            if (db_ver.Engine.UserVersion != Version5)
                throw new Exception("Invalid db version");
            db_ver.Dispose();
            Backup(pathToDb);

            using var db = new LiteDatabase(connectionString);
            
            var tezSymbols = new[] {"XTZ", "TZBTC", "KUSD"};
            
            var removedXtzTx = db.GetCollection("Transactions")
                .Find(Query.Where(nameof(WalletAddress.Currency), x => tezSymbols.Contains(x.AsString)));
            // .Delete());
            
            Log.Debug($"{removedXtzTx.Count()} XTZ and XTZ Tokens transactions removed by migration");

            Shrink(db, sessionPassword);
            // UpdateVersion(db: db, fromVersion: Version5, toVersion: Version6);
            
            return Version6;
        }

        private static void Backup(string pathToDb)
        {
            var dbDirectory = Path.GetDirectoryName(pathToDb);
            var pathToBackups = $"{dbDirectory}/backups";

            if (!Directory.Exists(pathToBackups))
                Directory.CreateDirectory(pathToBackups);

            var pathToBackup = $"{pathToBackups}/data_{DateTime.Now:ddMMyyyy_HHmmss_fff}";
            File.Copy(Path.GetFullPath(pathToDb), Path.GetFullPath(pathToBackup));
        }

        private static void Shrink(LiteDatabase db, string sessionPassword)
        {
            // db.Shrink(sessionPassword);

            Log.Debug("Db successfully shrinked");
        }

        private static void UpdateVersion(LiteDatabase db, ushort fromVersion, ushort toVersion)
        {
            db.Engine.UserVersion = toVersion;

            Log.Debug(
                "Db successfully update from version {@from} to version {@to}",
                fromVersion,
                toVersion);
        }
    }
}