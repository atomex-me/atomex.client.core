using Atomix.Common.Proto;
using Atomix.MarketData;

namespace Atomix.Api.Proto
{
    public class SubscribeScheme : ProtoScheme
    {
        public const int MessageId = 10;

        public SubscribeScheme()
            : base(MessageId)
        {
            Model.Add(typeof(Subscription), true)
                //.AddRequired(nameof(Subscription.Symbol))
                .AddRequired(nameof(Subscription.Type));
        }
    }
}