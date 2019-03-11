using Atomix.Common.Proto;
using Atomix.MarketData;

namespace Atomix.Api.Proto
{
    public class UnsubscribeScheme : ProtoScheme
    {
        public const int MessageId = 11;

        public UnsubscribeScheme()
            : base(MessageId)
        {
            Model.Add(typeof(Subscription), true)
                //.AddRequired(nameof(Subscription.Symbol))
                .AddRequired(nameof(Subscription.Type));
        }
    }
}