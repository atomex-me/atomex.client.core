using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
{
    public class OrderStatusScheme : ProtoScheme<Request<Order>>
    {
        public OrderStatusScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Order), true)
                .AddRequired(nameof(Order.Id))
                .AddRequired(nameof(Order.Symbol))
                .AddRequired(nameof(Order.Side));

            Model.Add(typeof(Request<Order>), true)
                .AddRequired(nameof(Request<Order>.Id))
                .AddRequired(nameof(Request<Order>.Data));
        }
    }
}