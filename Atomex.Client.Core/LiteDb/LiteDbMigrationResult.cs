using System.Collections.Generic;

using Atomex.Common;

namespace Atomex.LiteDb
{
    public enum Collections
    {
        Orders,
        Swaps,
        Transactions,
        TransactionsMetadata,
        Outputs,
        Addresses,
        TezosTokensContracts
    }

    public struct LiteDbMigrationRemoveAction
    {
        public Collections Collection { get; set; }
        public string Currency { get; set; }
    }

    public class LiteDbMigrationActionEqualityComparer : IEqualityComparer<LiteDbMigrationRemoveAction>
    {
        public bool Equals(LiteDbMigrationRemoveAction x, LiteDbMigrationRemoveAction y) => 
            x.Collection == y.Collection &&
            x.Currency == y.Currency;

        public int GetHashCode(LiteDbMigrationRemoveAction obj) =>
            obj.Collection.GetHashCode() ^ obj.Currency.GetHashCode();
    }

    public class LiteDbMigrationResult : List<LiteDbMigrationRemoveAction>
    {
        public Error? Error { get; }

        public void Add(Collections collections, string currency)
        {
            Add(new LiteDbMigrationRemoveAction { Collection = collections, Currency = currency });
        }
    }
}