using Atomex.Common.Proto;
using Atomex.Core;

namespace Atomex.Api.Proto
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