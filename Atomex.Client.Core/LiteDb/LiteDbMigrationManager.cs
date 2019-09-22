using System;
using System.IO;
using LiteDB;
using Serilog;

namespace Atomex.LiteDb
{
    public class LiteDbMigrationManager
    {
        public bool IsTransactionsReloadNeeded { get; private set; }

        public void MigrateIfNeed(
            string pathToDb,
            string sessionPassword,
            ushort targetVersion)
        {
            try
            {
                using (var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword}"))
                {
                    if (db.Engine.UserVersion == 0)
                    {
                        // backup firstly
                        var dbDirectory = Path.GetDirectoryName(pathToDb);
                        var pathToBackups = $"{dbDirectory}/backups";

                        if (!Directory.Exists(pathToBackups))
                            Directory.CreateDirectory(pathToBackups);

                        var pathToBackup = $"{pathToBackups}/data_{DateTime.Now:ddMMyyyy_HHmmss_fff}";
                        File.Copy(Path.GetFullPath(pathToDb), Path.GetFullPath(pathToBackup));

                        // first "raw" version, simply reload all transaction from blockchains
                        var removed = db.GetCollection("Transactions").Delete(Query.All());

                        Log.Debug($"{removed} transactions removed by migration");

                        db.Shrink(sessionPassword);

                        Log.Debug("Db successfully shrinked");

                        IsTransactionsReloadNeeded = true;
                    }

                    // update current version
                    db.Engine.UserVersion = targetVersion;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "LiteDb migration error");
            }
        }
    }
}