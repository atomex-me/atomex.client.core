using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
{
    public class OrderScheme : ProtoScheme<Response<Order>>
    {
        public OrderScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Order), true)
                .AddRequired(nameof(Order.Id))
                .AddRequired(nameof(Order.ClientOrderId))
                .AddRequired(nameof(Order.Symbol))
                .AddRequired(nameof(Order.TimeStamp))
                .AddRequired(nameof(Order.Price))
                .AddRequired(nameof(Order.LastPrice))
                .AddRequired(nameof(Order.Qty))
                .AddRequired(nameof(Order.LeaveQty))
                .AddRequired(nameof(Order.LastQty))
                .AddRequired(nameof(Order.Side))
                .AddRequired(nameof(Order.Type))
                .AddRequired(nameof(Order.Status));

            Model.Add(typeof(Response<Order>), true)
                .AddRequired(nameof(Response<Order>.RequestId))
                .AddRequired(nameof(Response<Order>.Data))
                .AddRequired(nameof(Response<Order>.EndOfMessage));
        }
    }
}