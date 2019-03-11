using System;
using Atomix.Core;
using Atomix.Core.Entities;

namespace Atomix.MarketData
{
    public class AnonymousOrder
    {
        public Guid OrderId { get; set; }
        public int SymbolId { get; set; }
        public DateTime TimeStamp { get; set; }
        public decimal Price { get; set; }
        public decimal LastPrice { get; set; }
        public decimal Qty { get; set; }
        public decimal LeaveQty { get; set; }
        public decimal LastQty { get; set; }
        public Side Side { get; set; }
        public OrderType Type { get; set; }
        public OrderStatus Status { get; set; }
        public bool EndOfTransaction { get; set; }

        public AnonymousOrder()
        {
        }

        public AnonymousOrder(Order order)
        {
            OrderId          = order.OrderId;
            SymbolId         = order.SymbolId;
            TimeStamp        = order.TimeStamp;
            Price            = order.Price;
            LastPrice        = order.LastPrice;
            Qty              = order.Qty;
            LeaveQty         = order.LeaveQty;
            LastQty          = order.LastQty;
            Side             = order.Side;
            Type             = order.Type;
            Status           = order.Status;
            EndOfTransaction = order.EndOfTransaction;
        }
    }
}