using System;
using Atomex.Core;

namespace Atomex.MarketData
{
    public class AnonymousOrder
    {
        public long OrderId { get; set; }
        public string Symbol { get; set; }
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
            OrderId          = order.Id;
            Symbol           = order.Symbol;
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