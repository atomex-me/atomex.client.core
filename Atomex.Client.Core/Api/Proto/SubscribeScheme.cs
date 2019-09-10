using System.Collections.Generic;
using Atomex.Common.Proto;
using Atomex.MarketData;

namespace Atomex.Api.Proto
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