using Atomix.Common.Proto;
using Atomix.MarketData;

namespace Atomix.Api.Proto
{
    public class EntriesScheme : ProtoScheme
    {
        public const int MessageId = 13;

        public EntriesScheme()
            : base(MessageId)
        {
            Model.Add(typeof(Entry), true)
                .AddRequired(nameof(Entry.TransactionId))
                .AddRequired(nameof(Entry.SymbolId))
                .AddRequired(nameof(Entry.Side))
                .AddRequired(nameof(Entry.Price))
                .AddRequired(nameof(Entry.QtyProfile));
        }
    }
}