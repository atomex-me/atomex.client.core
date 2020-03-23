using System.Collections.Generic;
using Atomex.Common.Proto;
using Atomex.MarketData;

namespace Atomex.Api.Proto
{
    public class QuotesScheme : ProtoScheme<List<Quote>>
    {
        public QuotesScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Quote), true)
                .AddRequired(nameof(Quote.Symbol))
                .AddRequired(nameof(Quote.TimeStamp))
                .AddRequired(nameof(Quote.Bid))
                .AddRequired(nameof(Quote.Ask));
        }
    }
}