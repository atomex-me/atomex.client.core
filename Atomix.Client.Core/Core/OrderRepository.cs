using System;
using System.Collections.Generic;
using Atomix.Common;
using Atomix.Core.Entities;
using Serilog;

namespace Atomix.Core
{
    public class OrderRepository
    {
        private readonly Dictionary<string, Order> _pendingOrders;
        private readonly Dictionary<Guid, Order> _orders;
        private readonly Dictionary<Guid, List<Order>> _ordersHistory;

        public OrderRepository()
        {
            _pendingOrders = new Dictionary<string, Order>();
            _orders = new Dictionary<Guid, Order>();
            _ordersHistory = new Dictionary<Guid, List<Order>>();
        }

        public bool AddOrder(Order order)
        {
            if (_pendingOrders.ContainsKey(order.ClientOrderId)) {
                Log.Error("Order with id {@clientOrderId} already exists!", order.ClientOrderId);
                return false;
            }

            _pendingOrders.Add(order.ClientOrderId, order);

            return true;
        }

        public bool UpdateOrder(Order order)
        {
            if (order.Status == OrderStatus.Placed || order.Status == OrderStatus.Rejected)
            {
                if (_pendingOrders.TryGetValue(order.ClientOrderId, out var pendingOrder))
                {
                    if (!order.IsContinuationOf(pendingOrder))
                    {
                        Log.Error("Order is not continuation of saved pending order! Order: {@order}, pending order: {@pendingOrder}", order, pendingOrder);
                        return false;
                    }

                    // move pending order to history
                    _pendingOrders.Remove(order.ClientOrderId);
                    AddToHistory(pendingOrder);
                }
                else
                {
                    // probably a different device order
                    Log.Information("Probably order form another device: {@order}", order);
                }

                if (_orders.TryGetValue(order.OrderId, out var activeOrder))
                {
                    Log.Error("Order is not continuation of saved active order! Order: {@order}, pending order: {@activeOrder}", order, activeOrder);
                    return false;
                }
            }
            else // order.Status != OrderStatus.Placed && order.Status != OrderStatus.Rejected
            {
                if (_orders.TryGetValue(order.OrderId, out var activeOrder))
                {
                    if (!order.IsContinuationOf(activeOrder))
                    {
                        Log.Error("Order is not continuation of saved active order! Order: {@order}, active order: {@activeOrder}", order, activeOrder);
                        return false;
                    }

                    // move current active order to history
                    _orders.Remove(order.OrderId);
                    AddToHistory(activeOrder);
                }
            }

            if (order.Status == OrderStatus.Placed || order.Status == OrderStatus.PartiallyFilled) {
                // add to active
                _orders.Add(order.OrderId, order);
            } else {
                // move directly to history
                AddToHistory(order);
            }

            return true;
        }

        private void AddToHistory(Order order)
        {
            if (_ordersHistory.TryGetValue(order.OrderId, out var orders)) {
                orders.Add(order);
            } else {
                _ordersHistory.Add(order.OrderId, new List<Order> { order });
            }
        }
    }
}