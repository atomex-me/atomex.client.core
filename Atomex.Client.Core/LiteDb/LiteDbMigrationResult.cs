using System.Collections.Generic;

using Atomex.Common;

namespace Atomex.LiteDb
{
    public enum MigrationEntityType
    {
        Orders,
        Swaps,
        Transactions,
        TransactionsMetadata,
        Outputs,
        Addresses,
        TezosTokensContracts,
    }

    public struct LiteDbMigrationChange
    {
        public MigrationEntityType EntityType { get; set; }
        public string Currency { get; set; }
    }

    public class LiteDbMigrationChangeEqualityComparer : IEqualityComparer<LiteDbMigrationChange>
    {
        public bool Equals(LiteDbMigrationChange x, LiteDbMigrationChange y) => 
            x.EntityType == y.EntityType &&
            x.Currency == y.Currency;

        public int GetHashCode(LiteDbMigrationChange obj) =>
            obj.EntityType.GetHashCode() ^ obj.Currency.GetHashCode();
    }

    public class LiteDbMigrationResult : List<LiteDbMigrationChange>
    {
        public Error? Error { get; }

        public void Add(MigrationEntityType entityType, string currency)
        {
            Add(new LiteDbMigrationChange { EntityType = entityType, Currency = currency });
        }
    }
}