using Atomix.Common.Proto;
using Atomix.MarketData;

namespace Atomix.Api.Proto
{
    public class QuotesScheme : ProtoScheme
    {
        public const int MessageId = 12;

        public QuotesScheme()
            : base(MessageId)
        {
            Model.Add(typeof(Quote), true)
                .AddRequired(nameof(Quote.SymbolId))
                .AddRequired(nameof(Quote.TimeStamp))
                .AddRequired(nameof(Quote.Bid))
                .AddRequired(nameof(Quote.Ask));
        }
    }
}