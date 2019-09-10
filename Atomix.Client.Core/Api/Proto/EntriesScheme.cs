using System.Collections.Generic;
using Atomix.Common.Proto;
using Atomix.MarketData;

namespace Atomix.Api.Proto
{
    public class EntriesScheme : ProtoScheme<List<Entry>>
    {
        public EntriesScheme(byte messageId)
            : base(messageId)
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