using System;
using Atomix.Core.Entities;

namespace Atomix.Core
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