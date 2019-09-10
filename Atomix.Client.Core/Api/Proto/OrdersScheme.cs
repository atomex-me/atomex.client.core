using Atomix.Common.Proto;
using Atomix.Core;
using Atomix.Core.Entities;

namespace Atomix.Api.Proto
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