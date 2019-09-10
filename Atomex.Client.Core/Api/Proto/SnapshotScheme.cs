using Atomex.Common.Proto;
using Atomex.MarketData;

namespace Atomex.Api.Proto
{
    public class SnapshotScheme : ProtoScheme<Snapshot>
    {
        public SnapshotScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Entry), true)
                .AddRequired(nameof(Entry.Side))
                .AddRequired(nameof(Entry.Price))
                .AddRequired(nameof(Entry.QtyProfile));

            Model.Add(typeof(Snapshot), true)
                .AddRequired(nameof(Snapshot.LastTransactionId))
                .AddRequired(nameof(Snapshot.SymbolId))
                .AddRequired(nameof(Snapshot.Entries));
        }
    }
}