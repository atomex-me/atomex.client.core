using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using LiteDB;
using Serilog;

using Atomex.Core;

namespace Atomex.LiteDb
{
    public static class LiteDbMigrations
    {
        public const ushort LastVersion = Version10;

        public const ushort Version0 = 0;
        public const ushort Version1 = 1;
        public const ushort Version2 = 2;
        public const ushort Version3 = 3;
        public const ushort Version4 = 4;
        public const ushort Version5 = 5;
        public const ushort Version6 = 6;
        public const ushort Version7 = 7;
        public const ushort Version8 = 8;
        public const ushort Version9 = 9;
        public const ushort Version10 = 10;

        public static ushort MigrateFrom_0_to_1(
            string pathToDb,
            string sessionPassword)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive";

            using (var dbVer = new LiteDatabase(connectionString))
            {
                if (dbVer.Engine.UserVersion != Version0)
                    throw new Exception("Invalid db version");
            }

            Backup(pathToDb);

            using var db = new LiteDatabase(connectionString);

            // first "raw" version, simply reload all transaction from blockchain
            var removed = db.GetCollection(LiteDbLocalStorage.TransactionCollectionName).Delete(Query.All());

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
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive";

            using (var dbVer = new LiteDatabase(connectionString))
            {
                if (dbVer.Engine.UserVersion != Version1)
                    throw new Exception("Invalid db version");

                if (network != Network.TestNet)
                {
                    UpdateVersion(db: dbVer, fromVersion: Version1, toVersion: Version2);
                    return Version2;
                }
            }

            Backup(pathToDb);

            using var db = new LiteDatabase(connectionString);

            // remove all tezos transactions
            db.GetCollection(LiteDbLocalStorage.TransactionCollectionName).Delete(Query.EQ("Currency", "XTZ"));

            // remove all tezos addresses
            db.GetCollection(LiteDbLocalStorage.AddressesCollectionName).Delete(Query.EQ("Currency", "XTZ"));

            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version1, toVersion: Version2);

            return Version2;
        }

        public static ushort MigrateFrom_2_to_3(
            string pathToDb,
            string sessionPassword,
            Network network)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive";

            using (var dbVer = new LiteDatabase(connectionString))
            {
                if (dbVer.Engine.UserVersion != Version2)
                    throw new Exception("Invalid db version");
            }

            Backup(pathToDb);

            using var db = new LiteDatabase(connectionString);

            // update wallet addresses
            var addressesCollection = db.GetCollection(LiteDbLocalStorage.AddressesCollectionName);
            var addresses = addressesCollection.FindAll().ToList();

            foreach (var address in addresses)
            {
                address["Address"] = address["_id"].AsString;
                address["_id"] = $"{address["_id"].AsString}:{address["Currency"].AsString}";
            }

            addressesCollection.Delete(Query.All());
            var inserted = addressesCollection.Upsert(addresses);

            // update transactions
            var transactionsCollection = db.GetCollection(LiteDbLocalStorage.TransactionCollectionName);
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
                db.GetCollection(LiteDbLocalStorage.TransactionCollectionName).Delete(Query.EQ("Currency", "XTZ"));
                db.GetCollection(LiteDbLocalStorage.AddressesCollectionName).Delete(Query.EQ("Currency", "XTZ"));
            }

            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version2, toVersion: Version3);

            return Version3;
        }

        public static ushort MigrateFrom_3_to_4(
            string pathToDb,
            string sessionPassword)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive";

            using (var dbVer = new LiteDatabase(connectionString))
            {
                if (dbVer.Engine.UserVersion != Version3)
                    throw new Exception("Invalid db version");
            }

            Backup(pathToDb);

            using var db = new LiteDatabase(connectionString);

            var currencies = new[] { "BTC", "LTC", "ETH", "XTZ", "USDT", "FA12" };

            var singleSuffixes = currencies.Select(c => $":{c}");
            var doubleSuffixes = currencies.Select(c => $":{c}:{c}");

            // fix wallet addresses
            var addressesCollection = db.GetCollection(LiteDbLocalStorage.AddressesCollectionName);
            var addresses = addressesCollection.FindAll().ToList();

            foreach (var addressInBson in addresses)
            {
                var address = addressInBson["Address"].AsString;

                foreach (var suffix in singleSuffixes)
                {
                    if (address.EndsWith(suffix))
                    {
                        address = address[..^suffix.Length];
                        addressInBson["Address"] = address;
                        break;
                    }
                }

                var id = addressInBson["_id"].AsString;

                foreach (var suffix in doubleSuffixes)
                {
                    if (id.EndsWith(suffix))
                    {
                        id = id[..^(suffix.Length / 2)];
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
            var transactionsCollection = db.GetCollection(LiteDbLocalStorage.TransactionCollectionName);
            var transactions = transactionsCollection.FindAll().ToList();

            foreach (var transactionInBson in transactions)
            {
                var txId = transactionInBson["TxId"].AsString;

                foreach (var suffix in singleSuffixes)
                {
                    if (txId.EndsWith(suffix))
                    {
                        txId = txId[..^suffix.Length];
                        transactionInBson["TxId"] = txId;
                        break;
                    }
                }

                var id = transactionInBson["_id"].AsString;

                foreach (var suffix in doubleSuffixes)
                {
                    if (id.EndsWith(suffix))
                    {
                        id = id[..^(suffix.Length / 2)];
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
            string sessionPassword)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive";

            using (var db_ver = new LiteDatabase(connectionString))
            {
                if (db_ver.Engine.UserVersion != Version4)
                    throw new Exception("Invalid db version");
            }

            Backup(pathToDb);

            using var db = new LiteDatabase(connectionString);

            // fix outputs
            var outputsCollection = db.GetCollection(LiteDbLocalStorage.OutputsCollectionName);
            var deletedOutputs = outputsCollection.Delete(Query.EQ("Currency", "BTC"));

            // fix transactions
            var transactionsCollection = db.GetCollection(LiteDbLocalStorage.TransactionCollectionName);
            var transactions = transactionsCollection.Delete(Query.EQ("Currency", "BTC"));

            // fix addresses
            var addressesCollection = db.GetCollection(LiteDbLocalStorage.AddressesCollectionName);
            var addresses = addressesCollection.Delete(Query.EQ("Currency", "BTC"));

            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version4, toVersion: Version5);

            return Version5;
        }

        public static ushort MigrateFrom_5_to_6(
            string pathToDb,
            string sessionPassword)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive";

            using (var db_ver = new LiteDatabase(connectionString))
            {
                if (db_ver.Engine.UserVersion != Version5)
                    throw new Exception("Invalid db version");
            }

            Backup(pathToDb);

            using var db = new LiteDatabase(connectionString);

            var tezSymbols = new[] { "XTZ", "TZBTC", "KUSD" };

            var removedXtzTx = db.GetCollection(LiteDbLocalStorage.TransactionCollectionName)
                .Delete(Query.Where(nameof(WalletAddress.Currency), x => tezSymbols.Contains(x.AsString)));

            Log.Debug($"{removedXtzTx} XTZ and tez tokens transactions removed by migration");

            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version5, toVersion: Version6);

            return Version6;
        }

        public static ushort MigrateFrom_6_to_7(
            string pathToDb,
            string sessionPassword)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive";

            using (var db_ver = new LiteDatabase(connectionString))
            {
                if (db_ver.Engine.UserVersion != Version6)
                    throw new Exception("Invalid db version");
            }

            Backup(pathToDb);

            using var db = new LiteDatabase(connectionString);

            var tezosTokens = new[] { "TZBTC", "KUSD" };

            var removedXtzTx = db
                .GetCollection(LiteDbLocalStorage.TransactionCollectionName)
                .Delete(Query.Where(nameof(WalletAddress.Currency), x => tezosTokens.Contains(x.AsString)));

            Log.Debug($"{removedXtzTx} Tezos tokens transactions removed by migration");

            var removedXtzAddresses = db
                .GetCollection(LiteDbLocalStorage.AddressesCollectionName)
                .Delete(Query.Where(nameof(WalletAddress.Currency), x => tezosTokens.Contains(x.AsString)));

            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version6, toVersion: Version7);

            return Version7;
        }

        public static ushort MigrateFrom_7_to_8(
            string pathToDb,
            string sessionPassword)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive";

            using (var db_ver = new LiteDatabase(connectionString))
            {
                if (db_ver.Engine.UserVersion != Version7)
                    throw new Exception("Invalid db version");
            }

            Backup(pathToDb);

            using var db = new LiteDatabase(connectionString);

            var addressesCollection = db.GetCollection(LiteDbLocalStorage.AddressesCollectionName);
            var xtzAddresses = addressesCollection
                .Find(Query.EQ("Currency", "XTZ"))
                .ToList();

            foreach (var xtzAddress in xtzAddresses)
                xtzAddress.Add("KeyType", TezosConfig.Bip32Ed25519Key);

            var upserted = addressesCollection.Upsert(xtzAddresses);

            var tezosTokensAddressesCollection = db.GetCollection(LiteDbLocalStorage.TezosTokensAddresses);
            var tezosTokensAddresses = tezosTokensAddressesCollection
                .FindAll()
                .ToList();

            foreach (var tezosTokenAddress in tezosTokensAddresses)
                tezosTokenAddress.Add("KeyType", TezosConfig.Bip32Ed25519Key);

            upserted = tezosTokensAddressesCollection.Upsert(tezosTokensAddresses);

            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version7, toVersion: Version8);

            return Version8;
        }

        public static ushort MigrateFrom_8_to_9(
            string pathToDb,
            string sessionPassword)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive";

            using (var db_ver = new LiteDatabase(connectionString))
                if (db_ver.Engine.UserVersion != Version8)
                    throw new Exception("Invalid db version");

            Backup(pathToDb);

            using var db = new LiteDatabase(connectionString);

            // // remove all tezos addresses, transactions and contracts
            var removedTokenAddresses = db
                .GetCollection(LiteDbLocalStorage.TezosTokensAddresses)
                .Delete(Query.All());
            
            var removedTransfers = db
                .GetCollection(LiteDbLocalStorage.TezosTokensTransfers)
                .Delete(Query.All());
            
            var removedContracts = db
                .GetCollection(LiteDbLocalStorage.TezosTokensContracts)
                .Delete(Query.All());
            
            var removedTransactions = db
                .GetCollection(LiteDbLocalStorage.TransactionCollectionName)
                .Delete(Query.EQ(nameof(WalletAddress.Currency), "XTZ"));
            
            var removedAddresses = db
                .GetCollection(LiteDbLocalStorage.AddressesCollectionName)
                .Delete(Query.EQ(nameof(WalletAddress.Currency), "XTZ"));
            
            var transactionsCollection = db.GetCollection(LiteDbLocalStorage.TransactionCollectionName);
            var usdtTransactions = transactionsCollection
                .Find(Query.EQ(nameof(WalletAddress.Currency), "USDT"))
                .ToList();

            foreach (var transaction in usdtTransactions)
            {
                transaction["GasUsed"] = transaction["GasLimit"];
            }

            transactionsCollection.Delete(Query.EQ(nameof(WalletAddress.Currency), "USDT"));
            transactionsCollection.Upsert(usdtTransactions);

            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version8, toVersion: Version9);

            return Version9;
        }

        public static ushort MigrateFrom_9_to_10(string pathToDb, string sessionPassword)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Mode=Exclusive";

            using var db = new LiteDatabase(connectionString);

            if (db.Engine.UserVersion != Version9)
                throw new Exception("Invalid db version");

            var removedAddressesCount = db.GetCollection(LiteDbLocalStorage.TezosTokensAddresses)
                .Delete(Query.Or(
                    Query.EQ("Currency", "KUSD"),
                    Query.EQ("Currency", "TZBTC"),
                    Query.EQ("Currency", "USDT_XTZ")));

            Log.Debug("Migration from v9 to v10: {@count} invalid tezos token addresses deleted", removedAddressesCount);
            
            Shrink(db, sessionPassword);
            UpdateVersion(db: db, fromVersion: Version9, toVersion: Version10);

            return Version10;
        }

        private static void Backup(string pathToDb)
        {
            var dbDirectory = Path.GetDirectoryName(pathToDb);
            var pathToBackups = $"{dbDirectory}/backups";

            if (!Directory.Exists(pathToBackups))
                Directory.CreateDirectory(pathToBackups);

            var pathToBackup = $"{pathToBackups}/data_{DateTime.Now:ddMMyyyy_HHmmss_fff}";
            var fullPathToDb = Path.GetFullPath(pathToDb);
            var fullPathToBackup = Path.GetFullPath(pathToBackup);

            File.Copy(fullPathToDb, fullPathToBackup);
        }

        public static void Shrink(
            LiteDatabase db,
            string sessionPassword)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return;

                db.Shrink(sessionPassword);
                Log.Debug("Db successfully shrinked");
            }
            catch (Exception)
            {
                Log.Error("Db shrink error");
            }
        }

        private static void UpdateVersion(
            LiteDatabase db,
            ushort fromVersion,
            ushort toVersion)
        {
            db.Engine.UserVersion = toVersion;

            Log.Debug(
                "Db successfully update from version {@from} to version {@to}",
                fromVersion,
                toVersion);
        }
    }
}