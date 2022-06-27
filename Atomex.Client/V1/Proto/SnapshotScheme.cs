using Atomex.Client.V1.Common;
using Atomex.MarketData.Entities;

namespace Atomex.Client.V1.Proto
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
                .AddRequired(nameof(Snapshot.Symbol))
                .AddRequired(nameof(Snapshot.Entries));
        }
    }
}