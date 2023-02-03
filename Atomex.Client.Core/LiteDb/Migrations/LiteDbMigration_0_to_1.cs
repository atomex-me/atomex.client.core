using System.IO;

using LiteDB;

using Atomex.Core;
using Atomex.Blockchain.Bitcoin;

namespace Atomex.LiteDb.Migrations
{
    public class LiteDbMigration_0_to_1
    {
        private const string Temp = "temp";

        public static LiteDbMigrationResult Migrate(string pathToDb, string sessionPassword)
        {
            var oldConnectionString = $"FileName={pathToDb};Password={sessionPassword};Connection=direct;Upgrade=true";
            var newConnectionString = $"FileName={pathToDb}.{Temp};Password={sessionPassword};Connection=direct";

            using (var oldDb = new LiteDatabase(oldConnectionString))
            using (var newDb = new LiteDatabase(newConnectionString))
            {
                // wallet addresses
                foreach (var oldAddress in oldDb.GetCollection("Addresses").FindAll())
                {
                    var addresses = newDb.GetCollection<WalletAddress>("Addresses");

                    addresses.Upsert(new WalletAddress { });
                }

                // bitcoin based outputs
                foreach (var oldOutput in oldDb.GetCollection("Outputs").FindAll())
                {
                    var outputs = newDb.GetCollection<BitcoinTxOutput>("Outputs");

                    outputs.Upsert(new BitcoinTxOutput { });
                }

                // bitcoin based transactions
                // ethereum transactions
                foreach (var oldTx in oldDb.GetCollection("Transactions").FindAll())
                {
                    var txs = newDb.GetCollection("Transactions");

                    //txs.Upsert(null);
                }

                // orders
                foreach (var oldOrder in oldDb.GetCollection("Orders").FindAll())
                {
                    var orders = newDb.GetCollection<Order>("Orders");

                    orders.Upsert(new Order { });
                }

                // swaps
                foreach (var oldSwap in oldDb.GetCollection("Swaps").FindAll())
                {
                    var orders = newDb.GetCollection<Swap>("Swaps");

                    orders.Upsert(new Swap { });
                }

                newDb.UserVersion = LiteDbMigrationManager.Version1;
            };

            File.Delete(pathToDb);
            File.Move($"{pathToDb}.{Temp}", pathToDb);

            return new LiteDbMigrationResult
            {
                new LiteDbMigrationAction { Collection = Collections.Transactions, Currency = "XTZ" },
                new LiteDbMigrationAction { Collection = Collections.Transactions, Currency = "USDT" },
                new LiteDbMigrationAction { Collection = Collections.Transactions, Currency = "WBTC" },
                new LiteDbMigrationAction { Collection = Collections.Transactions, Currency = "TBTC" },
                new LiteDbMigrationAction { Collection = Collections.TezosTokensTransfers, Currency = "ALL" },
                new LiteDbMigrationAction { Collection = Collections.TezosTokensAddresses, Currency = "ALL" },
                new LiteDbMigrationAction { Collection = Collections.TezosTokensContracts, Currency = "ALL" },
            };
        }
    }
}