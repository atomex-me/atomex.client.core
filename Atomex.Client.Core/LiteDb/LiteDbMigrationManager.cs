//using System;
//using System.Collections.Generic;
//using System.IO;
//using LiteDB;
//using Serilog;
//using Atomex.Core;
//using Atomex.Client.Entities;

//namespace Atomex.LiteDb
//{
//    public enum MigrationActionType
//    {
//        XtzTransactionsDeleted,
//        XtzTokensDataDeleted
//    }

//    public static class LiteDbMigrationManager
//    {
//        //public static void Migrate(
//        //    string pathToDb,
//        //    string sessionPassword,
//        //    Network network,
//        //    Action<MigrationActionType> migrationComplete = null)
//        //{
//        //    var migrationActions = new HashSet<MigrationActionType>();

//        //    try
//        //    {
//        //        if (!File.Exists(pathToDb))
//        //        {
//        //            CreateDataBase(
//        //                pathToDb: pathToDb,
//        //                sessionPassword: sessionPassword,
//        //                targetVersion: LiteDbMigrations.LastVersion);

//        //            return;
//        //        }

//        //        RemoveCanceledOrders(pathToDb, sessionPassword);

//        //        var currentVersion = GetDataBaseVersion(pathToDb, sessionPassword);

//        //        if (currentVersion == LiteDbMigrations.Version0)
//        //            currentVersion = LiteDbMigrations.MigrateFrom_0_to_1(pathToDb, sessionPassword);

//        //        if (currentVersion == LiteDbMigrations.Version1)
//        //            currentVersion = LiteDbMigrations.MigrateFrom_1_to_2(pathToDb, sessionPassword, network);

//        //        if (currentVersion == LiteDbMigrations.Version2)
//        //            currentVersion = LiteDbMigrations.MigrateFrom_2_to_3(pathToDb, sessionPassword, network);

//        //        if (currentVersion == LiteDbMigrations.Version3)
//        //            currentVersion = LiteDbMigrations.MigrateFrom_3_to_4(pathToDb, sessionPassword);

//        //        if (currentVersion == LiteDbMigrations.Version4)
//        //            currentVersion = LiteDbMigrations.MigrateFrom_4_to_5(pathToDb, sessionPassword);

//        //        if (currentVersion == LiteDbMigrations.Version5)
//        //        {
//        //            currentVersion = LiteDbMigrations.MigrateFrom_5_to_6(pathToDb, sessionPassword);
//        //            migrationActions.Add(MigrationActionType.XtzTransactionsDeleted);
//        //        }

//        //        if (currentVersion == LiteDbMigrations.Version6)
//        //        {
//        //            currentVersion = LiteDbMigrations.MigrateFrom_6_to_7(pathToDb, sessionPassword);
//        //            migrationActions.Add(MigrationActionType.XtzTokensDataDeleted);
//        //        }

//        //        if (currentVersion == LiteDbMigrations.Version7)
//        //            currentVersion = LiteDbMigrations.MigrateFrom_7_to_8(pathToDb, sessionPassword);

//        //        if (currentVersion == LiteDbMigrations.Version8)
//        //        {
//        //            currentVersion = LiteDbMigrations.MigrateFrom_8_to_9(pathToDb, sessionPassword);
//        //            migrationActions.Add(MigrationActionType.XtzTransactionsDeleted);
//        //        }

//        //        if (currentVersion == LiteDbMigrations.Version9)
//        //            currentVersion = LiteDbMigrations.MigrateFrom_9_to_10(pathToDb, sessionPassword);

//        //        if (currentVersion == LiteDbMigrations.Version10)
//        //        {
//        //            currentVersion = LiteDbMigrations.MigrateFrom_10_to_11(pathToDb, sessionPassword);
//        //            migrationActions.Add(MigrationActionType.XtzTokensDataDeleted);
//        //        }
//        //    }
//        //    catch (Exception e)
//        //    {
//        //        Log.Error(e, "LiteDb migration error");
//        //    }

//        //    foreach (var migrationAction in migrationActions)
//        //    {
//        //        try
//        //        {
//        //            migrationComplete?.Invoke(migrationAction);
//        //        }
//        //        catch (Exception e)
//        //        {
//        //            Log.Error(e, "LiteDb migration callback error for action {@action}", migrationAction);
//        //        }
//        //    }
//        //}

//        private static void RemoveCanceledOrders(
//            string pathToDb,
//            string sessionPassword)
//        {
//            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive");

//            if (db.CollectionExists(LiteDbLocalStorage.OrdersCollectionName))
//            {
//                var orders = db.GetCollection(LiteDbLocalStorage.OrdersCollectionName);

//                var removedOrdersCount = orders.Delete(
//                    Query.And(
//                        Query.EQ(nameof(Order.Status), OrderStatus.Canceled.ToString()),
//                        Query.EQ(nameof(Order.LastQty), 0m)));

//                Log.Debug("{@count} canceled orders were removed from db", removedOrdersCount);
//            }

//            LiteDbMigrations.Shrink(db, sessionPassword);
//        }

//        private static ushort GetDataBaseVersion(
//            string pathToDb,
//            string sessionPassword)
//        {
//            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive");

//            return db.Engine.UserVersion;
//        }

//        private static void CreateDataBase(
//            string pathToDb,
//            string sessionPassword,
//            ushort targetVersion)
//        {
//            using var db = new LiteDatabase($"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive");

//            db.Engine.UserVersion = targetVersion;
//        }
//    }
//}