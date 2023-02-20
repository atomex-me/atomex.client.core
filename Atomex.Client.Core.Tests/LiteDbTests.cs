using LiteDB;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Atomex.Client.Core.Tests
{
    public class LiteDbTests
    {
        private ITestOutputHelper console;

        public LiteDbTests(ITestOutputHelper output)
        {
            this.console = output;
        }

        [Fact]
        public void CanInclude()
        {
            using var db = new LiteDatabase(new ConnectionString() { Filename = ":temp:" });

            var txs = db.GetCollection("Transactions");

            txs.Insert(new BsonDocument {
                ["_id"] = "1",
                ["currency"] = "BTC",
                ["time"] = DateTime.Parse("12.06.2023"),
                ["metadata"] = new BsonDocument
                {
                    ["$id"] = "1",
                    ["$ref"] = "Metadata"
                }
            });

            txs.Insert(new BsonDocument
            {
                ["_id"] = "2",
                ["currency"] = "BTC",
                ["time"] = DateTime.Parse("11.06.2023"),
                ["metadata"] = new BsonDocument
                {
                    ["$id"] = "2",
                    ["$ref"] = "Metadata"
                }
            });

            txs.Insert(new BsonDocument
            {
                ["_id"] = "3",
                ["currency"] = "ETH",
                ["time"] = DateTime.Parse("11.06.2023"),
                ["metadata"] = new BsonDocument
                {
                    ["$id"] = "3",
                    ["$ref"] = "Metadata"
                }
            });

            txs.Insert(new BsonDocument
            {
                ["_id"] = "4",
                ["currency"] = "BTC",
                ["time"] = DateTime.Parse("14.06.2023"),
                ["metadata"] = new BsonDocument
                {
                    ["$id"] = "4",
                    ["$ref"] = "Metadata"
                }
            });

            var metadata = db.GetCollection("Metadata");

            metadata.Insert(new BsonDocument {
                ["_id"] = "1",
                ["amount"] = 123,
                ["type"] = "Self"
            });

            using var reader = db.Execute(
                "SELECT $ FROM Transactions INCLUDE $.metadata WHERE $.currency LIKE @0 ORDER BY $.time DESC", "BTC");


            while (reader.Read())
            {
                console.WriteLine(reader.Current.ToString());
            }
        }
    }
}