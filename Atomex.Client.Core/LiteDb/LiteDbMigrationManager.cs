using System;
using System.IO;

using LiteDB;
using Serilog;

using Atomex.Core;

namespace Atomex.LiteDb
{
    public enum MigrationActionType
    {
        XtzTransactionsDeleted,
        XtzTokensDataDeleted
    }

    public static class LiteDbMigrationManager
    {
        public static void Migrate(
            string pathToDb,
            string sessionPassword,
            Network network,
            Action<MigrationActionType> migrationComplete = null)
        {
            try
            {
                if (!File.Exists(pathToDb))
                {
                    CreateDataBase(
                        pathToDb: pathToDb,
                        sessionPassword: sessionPassword,
                        targetVersion: LiteDbMigrations.LastVersion);

                    return;
                }

                RemoveCanceledOrders(pathToDb, sessionPassword);

                var currentVersion = GetDataBaseVersion(pathToDb, sessionPassword);

                if (currentVersion == LiteDbMigrations.Version0)
                    currentVersion = LiteDbMigrations.MigrateFrom_0_to_1(pathToDb, sessionPassword);

                if (currentVersion == LiteDbMigrations.Version1)
                    currentVersion = LiteDbMigrations.MigrateFrom_1_to_2(pathToDb, sessionPassword, network);

                if (currentVersion == LiteDbMigrations.Version2)
                    currentVersion = LiteDbMigrations.MigrateFrom_2_to_3(pathToDb, sessionPassword, network);

                if (currentVersion == LiteDbMigrations.Version3)
                    currentVersion = LiteDbMigrations.MigrateFrom_3_to_4(pathToDb, sessionPassword);

                if (currentVersion == LiteDbMigrations.Version4)
                    currentVersion = LiteDbMigrations.MigrateFrom_4_to_5(pathToDb, sessionPassword);

                if (currentVersion == LiteDbMigrations.Version5)
                {
                    currentVersion = LiteDbMigrations.MigrateFrom_5_to_6(pathToDb, sessionPassword);
                    migrationComplete?.Invoke(MigrationActionType.XtzTransactionsDeleted);
                }

                if (currentVersion == LiteDbMigrations.Version6)
                {
                    currentVersion = LiteDbMigrations.MigrateFrom_6_to_7(pathToDb, sessionPassword);
                    migrationComplete?.Invoke(MigrationActionType.XtzTokensDataDeleted);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "LiteDb migration error");
            }
        }

        private static void RemoveCanceledOrders(
            string pathToDb,
            string sessionPassword)
        {
            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive");

            var totalOrders = db.GetCollection("Orders").Count();

            var removedOrders = db.DropCollection("Orders");

            // db.Shrink();
        }

        private static ushort GetDataBaseVersion(
            string pathToDb,
            string sessionPassword)
        {
            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive");

            return db.Engine.UserVersion;
        }

        private static void CreateDataBase(
            string pathToDb,
            string sessionPassword,
            ushort targetVersion)
        {
            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive");

            db.Engine.UserVersion = targetVersion;
        }
    }
}