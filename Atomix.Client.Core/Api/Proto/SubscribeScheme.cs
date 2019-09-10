using System.Collections.Generic;
using Atomix.Common.Proto;
using Atomix.MarketData;

namespace Atomix.Api.Proto
{
    public class UnsubscribeScheme : ProtoScheme<List<Subscription>>
    {
        public UnsubscribeScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Subscription), true)
                //.AddRequired(nameof(Subscription.Symbol))
                .AddRequired(nameof(Subscription.Type));
        }
    }
}