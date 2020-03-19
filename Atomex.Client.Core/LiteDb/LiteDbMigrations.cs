using System;
using System.IO;
using System.Linq;
using Atomex.Core;
using LiteDB;
using Serilog;

namespace Atomex.LiteDb
{
    public static class LiteDbMigrations
    {
        public const ushort Version0 = 0;
        public const ushort Version1 = 1;
        public const ushort Version2 = 2;
        public const ushort Version3 = 3;

        public static ushort MigrateFrom_0_to_1(string pathToDb, string sessionPassword)
        {
            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword}");
            
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
            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword}");
            
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
            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword}");
            
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
            db.Shrink(sessionPassword);

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