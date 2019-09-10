using System.Collections.Generic;
using Atomix.Common.Proto;
using Atomix.MarketData;

namespace Atomix.Api.Proto
{
    public class QuotesScheme : ProtoScheme<List<Quote>>
    {
        public QuotesScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Quote), true)
                .AddRequired(nameof(Quote.SymbolId))
                .AddRequired(nameof(Quote.TimeStamp))
                .AddRequired(nameof(Quote.Bid))
                .AddRequired(nameof(Quote.Ask));
        }
    }
}