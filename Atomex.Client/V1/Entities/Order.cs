using System;
using System.Collections.Generic;

using Atomex.Client.Entities;
using Atomex.Common;

namespace Atomex.Client.V1.Entities
{
    public class Order
    {
        public long Id { get; set; }
        public string ClientOrderId { get; set; }
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
        public IList<WalletAddress> FromWallets { get; set; }

        public string BaseCurrencyContract { get; set; }
        public string QuoteCurrencyContract { get; set; }
    }
}