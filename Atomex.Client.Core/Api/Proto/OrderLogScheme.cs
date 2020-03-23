using System.Collections.Generic;
using Atomex.Common.Proto;
using Atomex.MarketData;

namespace Atomex.Api.Proto
{
    public class OrderLogScheme : ProtoScheme<List<AnonymousOrder>>
    {
        public OrderLogScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(AnonymousOrder), true)
                .AddRequired(nameof(AnonymousOrder.OrderId))
                .AddRequired(nameof(AnonymousOrder.Symbol))
                .AddRequired(nameof(AnonymousOrder.TimeStamp))
                .AddRequired(nameof(AnonymousOrder.Price))
                .AddRequired(nameof(AnonymousOrder.LastPrice))
                .AddRequired(nameof(AnonymousOrder.Qty))
                .AddRequired(nameof(AnonymousOrder.LeaveQty))
                .AddRequired(nameof(AnonymousOrder.LastQty))
                .AddRequired(nameof(AnonymousOrder.Side))
                .AddRequired(nameof(AnonymousOrder.Type))
                .AddRequired(nameof(AnonymousOrder.Status))
                .AddRequired(nameof(AnonymousOrder.EndOfTransaction));
        }
    }
}