using System;

using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Common
{
    public class OrderEventArgs : EventArgs
    {
        public Order Order { get; }

        public OrderEventArgs(Order order)
        {
            Order = order;
        }
    }
}