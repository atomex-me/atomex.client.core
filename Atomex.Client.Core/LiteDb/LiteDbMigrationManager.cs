#nullable enable

using LiteDB;

using Atomex.LiteDb.Migrations;
using Atomex.Core;

namespace Atomex.LiteDb
{
    public static class LiteDbMigrationManager
    {
        public const int Version12 = 12;
        public const int CurrentVersion = Version12;

        public static LiteDbMigrationResult? Migrate(
            string pathToDb,
            string sessionPassword,
            Network network)
        {
            LiteDbMigrationResult? result = null;

            var dbVersion = GetDataBaseVersion(pathToDb, sessionPassword);

            if (dbVersion < Version12) // migrate to version12
            {
                result = LiteDbMigration_11_to_12.Migrate(pathToDb, sessionPassword, network);

                //dbVersion = Version1;
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