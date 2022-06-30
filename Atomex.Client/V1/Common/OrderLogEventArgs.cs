using System;
using System.Collections.Generic;

using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Common
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