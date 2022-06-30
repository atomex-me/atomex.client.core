using System.Collections.Generic;

using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
{
    public class SubscribeScheme : ProtoScheme<List<Subscription>>
    {
        public SubscribeScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Subscription), true)
                .AddRequired(nameof(Subscription.Type));
        }
    }
}