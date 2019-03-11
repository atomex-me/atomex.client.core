using Atomix.Common.Proto;
using Atomix.MarketData;

namespace Atomix.Api.Proto
{
    public class OrderLogScheme : ProtoScheme
    {
        public const int MessageId = 15;

        public OrderLogScheme()
            : base(MessageId)
        {
            Model.Add(typeof(AnonymousOrder), true)
                .AddRequired(nameof(AnonymousOrder.OrderId))
                .AddRequired(nameof(AnonymousOrder.SymbolId))
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