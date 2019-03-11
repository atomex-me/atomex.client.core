using Atomix.Common.Proto;
using Atomix.MarketData;

namespace Atomix.Api.Proto
{
    public class SnapshotScheme : ProtoScheme
    {
        public const int MessageId = 14;

        public SnapshotScheme()
            : base(MessageId)
        {
            Model.Add(typeof(Entry), true)
                .AddRequired(nameof(Entry.Side))
                .AddRequired(nameof(Entry.Price))
                .AddRequired(nameof(Entry.QtyProfile));

            Model.Add(typeof(Snapshot), true)
                .AddRequired(nameof(MarketData.Snapshot.LastTransactionId))
                .AddRequired(nameof(MarketData.Snapshot.SymbolId))
                .AddRequired(nameof(MarketData.Snapshot.Entries));
        }
    }
}