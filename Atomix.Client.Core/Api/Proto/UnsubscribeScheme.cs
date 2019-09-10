using System.Collections.Generic;
using Atomix.Common.Proto;
using Atomix.MarketData;

namespace Atomix.Api.Proto
{
    public class SubscribeScheme : ProtoScheme<List<Subscription>>
    {
        public SubscribeScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Subscription), true)
                //.AddRequired(nameof(Subscription.Symbol))
                .AddRequired(nameof(Subscription.Type));
        }
    }
}