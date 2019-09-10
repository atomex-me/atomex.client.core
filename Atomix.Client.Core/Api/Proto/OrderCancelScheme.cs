using Atomix.Common.Proto;
using Atomix.Core.Entities;

namespace Atomix.Api.Proto
{
    public class OrderCancelScheme : ProtoScheme<Order>
    {
        public OrderCancelScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Symbol), true)
                .AddRequired(nameof(Symbol.Name));

            Model.Add(typeof(Order), true)
                .AddRequired(nameof(Order.Id))
                .AddRequired(nameof(Order.Symbol))
                .AddRequired(nameof(Order.Side));
        }
    }
}