using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
{
    public class OrdersScheme : ProtoScheme<Request<Order>>
    {
        public OrdersScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Request<Order>), true)
                .AddRequired(nameof(Request<Order>.Id));
        }
    }
}