using LiteDB;

using Atomex.LiteDb.Migrations;
using Atomex.Core;

namespace Atomex.LiteDb
{
    public static class LiteDbMigrationManager
    {
        public const int Version0 = 0;
        public const int Version1 = 1;
        public const int CurrentVersion = Version1;

        public static LiteDbMigrationResult Migrate(
            string pathToDb,
            string sessionPassword,
            Network network)
        {
            LiteDbMigrationResult result = null;

            var dbVersion = GetDataBaseVersion(pathToDb, sessionPassword);

            if (dbVersion == Version0) // migrate to version1
            {
                result = LiteDbMigration_0_to_1.Migrate(pathToDb, sessionPassword, network);

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