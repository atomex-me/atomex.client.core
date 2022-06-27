using System.Collections.Generic;

using Atomex.Client.V1.Common;
using Atomex.MarketData.Entities;

namespace Atomex.Client.V1.Proto
{
    public class EntriesScheme : ProtoScheme<List<Entry>>
    {
        public EntriesScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Entry), true)
                .AddRequired(nameof(Entry.TransactionId))
                .AddRequired(nameof(Entry.Symbol))
                .AddRequired(nameof(Entry.Side))
                .AddRequired(nameof(Entry.Price))
                .AddRequired(nameof(Entry.QtyProfile));
        }
    }
}