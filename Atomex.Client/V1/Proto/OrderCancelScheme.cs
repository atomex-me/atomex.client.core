using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
{
    public class OrderCancelScheme : ProtoScheme<Order>
    {
        public OrderCancelScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Order), true)
                .AddRequired(nameof(Order.Id))
                .AddRequired(nameof(Order.Symbol))
                .AddRequired(nameof(Order.Side));
        }
    }
}