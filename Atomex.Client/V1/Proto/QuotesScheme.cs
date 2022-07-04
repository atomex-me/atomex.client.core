using System.Collections.Generic;

using Atomex.Client.V1.Common;
using Atomex.MarketData.Entities;

namespace Atomex.Client.V1.Proto
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