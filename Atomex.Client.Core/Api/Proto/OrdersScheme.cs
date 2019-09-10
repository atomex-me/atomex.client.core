using Atomex.Common.Proto;
using Atomex.Core;
using Atomex.Core.Entities;

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