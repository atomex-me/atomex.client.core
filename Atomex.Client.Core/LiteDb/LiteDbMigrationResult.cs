using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
        TezosTokensAddresses,
        TezosTokensTransfers,
        TezosTokensContracts
    }

    public class LiteDbMigrationAction
    {
        public Collections Collection { get; set; }
        public string Currency { get; set; }
    }

    public class LiteDbMigrationActionEqualityComparer : IEqualityComparer<LiteDbMigrationAction>
    {
        public bool Equals(LiteDbMigrationAction x, LiteDbMigrationAction y) => 
            x != null &&
            y != null &&
            x.Collection == y.Collection &&
            x.Currency == y.Currency;

        public int GetHashCode(LiteDbMigrationAction obj) =>
            obj.Collection.GetHashCode() ^ obj.Currency.GetHashCode();
    }

    public class LiteDbMigrationResult : IEnumerable<LiteDbMigrationAction>
    {
        public List<LiteDbMigrationAction> Removed { get; }
        public Error? Error { get; }

        public LiteDbMigrationResult()
        {
            Removed = new List<LiteDbMigrationAction>();
        }

        public void Add(LiteDbMigrationAction action)
        {
            Removed.Add(action);
        }

        public LiteDbMigrationResult Aggregate(LiteDbMigrationResult result)
        {
            Removed.AddRange(result.Removed);
            Removed.Distinct(new LiteDbMigrationActionEqualityComparer());

            return this;
        }

        public IEnumerator<LiteDbMigrationAction> GetEnumerator() => Removed.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Removed.GetEnumerator();
    }
}