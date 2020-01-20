using Atomex.Common.Proto;
using Atomex.Core;

namespace Atomex.Api.Proto
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