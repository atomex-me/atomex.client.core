using Atomex.Common.Proto;
using Atomex.Core;

namespace Atomex.Api.Proto
{
    public class OrderStatusScheme : ProtoScheme<Request<Order>>
    {
        public OrderStatusScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Symbol), true)
                .AddRequired(nameof(Symbol.Name));

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