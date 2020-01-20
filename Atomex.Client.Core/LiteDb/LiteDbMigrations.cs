using System;
using System.IO;
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

        public static ushort MigrateFrom_0_to_1(string pathToDb, string sessionPassword)
        {
            using (var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword}"))
            {
                if (db.Engine.UserVersion != Version0)
                    throw new Exception("Invalid db version");

                Backup(pathToDb);

                // first "raw" version, simply reload all transaction from blockchain
                var removed = db.GetCollection("Transactions").Delete(Query.All());

                Log.Debug($"{removed} transactions removed by migration");

                Shrink(db, sessionPassword);
                UpdateVersion(db: db, fromVersion: Version0, toVersion: Version1);
            }

            return Version1;
        }

        public static ushort MigrateFrom_1_to_2(
            string pathToDb,
            string sessionPassword,
            Network network)
        {
            using (var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword}"))
            {
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
            }

            return Version2;
        }

        private static void Backup(string pathToDb)
        {
            var dbDirectory = Path.GetDirectoryName(pathToDb);
            var pathToBackups = $"{dbDirectory}/backups";

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