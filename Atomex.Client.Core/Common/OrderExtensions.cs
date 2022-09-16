using System;

using Serilog;

using Atomex.Client.Entities;
using Atomex.Core;

namespace Atomex.Common
{
    public static class OrderExtensions
    {
        public static bool IsContinuationOf(this Order order, Order previousOrder)
        {
            if (previousOrder == null)
                throw new ArgumentNullException(nameof(previousOrder));

            // check basic fields
            if (order.ClientOrderId != previousOrder.ClientOrderId ||
                order.Symbol != previousOrder.Symbol ||
                order.Price != previousOrder.Price ||
                order.Qty != previousOrder.Qty ||
                order.Side != previousOrder.Side ||
                order.Type != previousOrder.Type)
            {
                return false;
            }

            // check statuses
            if (!order.Status.IsContinuationOf(previousOrder.Status))
                return false;

            // check leave qty
            if (order.LeaveQty > previousOrder.LeaveQty ||
                ((order.Status == OrderStatus.PartiallyFilled || order.Status == OrderStatus.Filled) && order.LeaveQty == previousOrder.LeaveQty))
                return false;

            return true;
        }

        public static bool IsContinuationOf(this OrderStatus status, OrderStatus previousStatus)
        {
            return status switch
            {
                OrderStatus.Pending => false,
                OrderStatus.Placed =>
                    previousStatus == OrderStatus.Pending,
                OrderStatus.PartiallyFilled or
                OrderStatus.Filled or
                OrderStatus.Canceled =>
                    previousStatus == OrderStatus.Pending || // allow orders without "Placed" status
                    previousStatus == OrderStatus.Placed ||
                    previousStatus == OrderStatus.PartiallyFilled,
                OrderStatus.Rejected => previousStatus == OrderStatus.Pending,
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
            };
        }

        public static bool VerifyOrder(this Order order, Order localOrder)
        {
            if (order.Status == OrderStatus.Pending)
            {
                if (localOrder != null)
                {
                    Log.Error("Order already pending");

                    return false;
                }
            }
            else
            {
                if (localOrder == null) // probably a different device order, allow to save, but mark as unapproved
                {
                    order.IsApproved = false;

                    Log.Information("Probably order from another device: {@order}",
                        order.ToString());
                }
                else
                {
                    if (localOrder.Status == OrderStatus.Rejected)
                    {
                        Log.Error("Order already rejected");

                        return false;
                    }

                    if (!order.IsContinuationOf(localOrder))
                    {
                        Log.Error("Order is not continuation of saved pending order! Order: {@order}, local order: {@localOrder}",
                            order.ToString(),
                            localOrder.ToString());

                        return false;
                    }
                }
            }

            return true;
        }

        public static void ForwardLocalParameters(this Order destination, Order source)
        {
            destination.IsApproved        = source.IsApproved;
            destination.MakerNetworkFee   = source.MakerNetworkFee;
            destination.FromAddress       = source.FromAddress;
            destination.FromOutputs       = source.FromOutputs;
            destination.ToAddress         = source.ToAddress;
            destination.RedeemFromAddress = source.RedeemFromAddress;
        }
    }
}