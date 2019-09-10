using System;
using System.Collections.Generic;

namespace Atomex.MarketData
{
    public class OrderLogEventArgs : EventArgs
    {
        public IList<AnonymousOrder> AnonymousOrders { get; }

        public OrderLogEventArgs(IList<AnonymousOrder> anonymousOrders)
        {
            AnonymousOrders = anonymousOrders;
        }
    }
}