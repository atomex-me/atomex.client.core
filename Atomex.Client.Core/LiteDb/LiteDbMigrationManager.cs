#nullable enable

using LiteDB;

using Atomex.LiteDb.Migrations;
using Atomex.Core;

namespace Atomex.LiteDb
{
    public static class LiteDbMigrationManager
    {
        public const int Version12 = 12;
        public const int Version13 = 13;
        public const int CurrentVersion = Version13;

        public static LiteDbMigrationResult? Migrate(
            string pathToDb,
            string sessionPassword,
            Network network)
        {
            LiteDbMigrationResult? result = null;

            var dbVersion = GetDataBaseVersion(pathToDb, sessionPassword);

            if (dbVersion < Version12) // migrate to version13
            {
                result = LiteDbMigration_11_to_13.Migrate(pathToDb, sessionPassword, network);
            }

            if (dbVersion == Version12)
            {
                result = LiteDbMigration_12_to_13.Migrate(pathToDb, sessionPassword, network);
            }

            return result;
        }

        public static int GetDataBaseVersion(string pathToDb, string sessionPassword)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Connection=direct;Upgrade=true";

            using var db = new LiteDatabase(connectionString);

            return db.UserVersion;
        }
    }
}