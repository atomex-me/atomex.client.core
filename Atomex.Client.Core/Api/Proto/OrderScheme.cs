using Atomex.Abstract;
using Atomex.Common.Proto;
using Atomex.Core;
using Atomex.Core.Entities;

namespace Atomex.Api.Proto
{
    public class OrderScheme : ProtoScheme<Response<Order>>
    {
        public OrderScheme(byte messageId, ICurrencies currencies)
            : base(messageId)
        {
            Model.Add(typeof(Currency), true)
                .AddCurrencies(currencies)
                .AddRequired(nameof(Currency.Name));

            Model.Add(typeof(Symbol), true)
                .AddRequired(nameof(Symbol.Name));

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