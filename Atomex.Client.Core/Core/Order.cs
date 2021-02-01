﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Atomex.Core
{
    public enum Side
    {
        Buy,
        Sell
    }

    public enum OrderStatus
    {
        Pending,
        Placed,
        PartiallyFilled,
        Filled,
        Canceled,
        Rejected
    }

    public enum OrderType
    {
        Return,
        FillOrKill,
        ImmediateOrCancel
    }

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
        public bool EndOfTransaction { get; set; }
        public bool IsApproved { get; set; } = true;
        public bool IsAlreadyCanceled { get; set; }
        public IList<WalletAddress> FromWallets { get; set; }
        public decimal MakerNetworkFee { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }

        public Order Clone()
        {
            return (Order)MemberwiseClone();
        }
    }
}