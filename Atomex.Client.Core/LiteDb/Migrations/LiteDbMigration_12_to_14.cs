using System.Linq;

using LiteDB;

using Atomex.Blockchain;
using Atomex.Blockchain.Bitcoin;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Erc20;
using Atomex.Blockchain.Tezos;
using Atomex.Common.Bson;
using Atomex.Core;
using Atomex.Wallets;

namespace Atomex.LiteDb.Migrations
{
    public class LiteDbMigration_12_to_14
    {
        private const string OrderIdKey = "OrderId";

        public static LiteDbMigrationResult Migrate(
            string pathToDb,
            string sessionPassword,
            Network network)
        {
            var connectionString = $"FileName={pathToDb};Password={sessionPassword};Connection=direct";

            var mapper = CreateBsonMapper(network);

            using var db = new LiteDatabase(connectionString, mapper);

            var currencies = new string[] { "btc", "ltc" };

            foreach (var c in currencies)
            {
                var collection = db.GetCollection($"{c}_txs");

                var txs = collection
                    .FindAll()
                    .ToList();

                foreach (var tx in txs)
                {
                    tx["_id"] = tx["TxId"];
                    tx.Remove("TxId");

                    tx["UserMetadata"]["$id"] = tx["_id"];
                }

                var removed = collection.DeleteAll();

                var upserted = collection.Upsert(txs);
            }

            db.UserVersion = LiteDbMigrationManager.Version14;

            return new LiteDbMigrationResult();
        }

        public static BsonMapper CreateBsonMapper(Network network)
        {
            var mapper = new BsonMapper()
                .UseSerializer(new BigIntegerToBsonSerializer())
                .UseSerializer(new JObjectToBsonSerializer())
                .UseSerializer(new CoinToBsonSerializer())
                .UseSerializer(new BitcoinTransactionSerializer(network));

            mapper.Entity<TokenBalance>()
                .Ignore(t => t.ParsedBalance)
                .Ignore(t => t.HasDescription)
                .Ignore(t => t.IsNft)
                .Ignore(t => t.ContractType);

            mapper.Entity<WalletAddress>()
                .Ignore(w => w.IsDisabled);

            mapper.Entity<BitcoinTxOutput>()
                .Id(o => o.UniqueId)
                .Ignore(o => o.Index)
                .Ignore(o => o.Value)
                .Ignore(o => o.IsValid)
                .Ignore(o => o.TxId)
                .Ignore(o => o.Type)
                .Ignore(o => o.IsSpent)
                .Ignore(o => o.IsPayToScript)
                .Ignore(o => o.IsSegWit);

            mapper.Entity<EthereumTransaction>()
                .Ignore(t => t.IsConfirmed);

            mapper.Entity<TezosOperation>()
                .Ignore(t => t.From)
                .Ignore(t => t.IsConfirmed);

            mapper.Entity<TezosTokenTransfer>()
                .Ignore(t => t.IsConfirmed)
                .Ignore(t => t.TokenId);

            mapper.Entity<Erc20Transaction>()
                .Ignore(t => t.IsConfirmed)
                .Ignore(t => t.TokenId);

            //mapper.Entity<TransactionMetadata>();

            mapper.Entity<Order>()
                .Id(o => o.ClientOrderId)
                .Field(o => o.Id, OrderIdKey);

            mapper.Entity<Swap>()
                .Ignore(s => s.SoldCurrency)
                .Ignore(s => s.PurchasedCurrency)
                .Ignore(s => s.IsComplete)
                .Ignore(s => s.IsRefunded)
                .Ignore(s => s.IsCanceled)
                .Ignore(s => s.IsUnsettled)
                .Ignore(s => s.IsActive)
                .Ignore(s => s.IsAcceptor)
                .Ignore(s => s.HasPartyPayment);

            return mapper;
        }
    }
}