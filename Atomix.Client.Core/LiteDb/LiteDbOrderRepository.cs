using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Abstract;
using Atomix.Core.Entities;
using LiteDB;
using Serilog;

namespace Atomix.LiteDb
{
    public class LiteDbOrderRepository : LiteDbRepository, IOrderRepository
    {
        public const string OrdersCollectionName = "orders";

        public LiteDbOrderRepository(string pathToDb, SecureString password)
            : base(pathToDb, password)
        {
        }

        public Task<bool> AddOrderAsync(Order order)
        {
            try
            {
                if (!CheckOrder(order))
                    return Task.FromResult(false);

                using (var db = new LiteDatabase(ConnectionString))
                {
                    var orders = db.GetCollection<Order>(OrdersCollectionName);

                    orders.EnsureIndex(o => o.OrderId);
                    orders.EnsureIndex(o => o.ClientOrderId);
                    orders.Insert(order);
                }

                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error adding order");
            }

            return Task.FromResult(false);
        }

        private IEnumerable<Order> GetPendingOrders(string clientOrderId)
        {
            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var orders = db.GetCollection<Order>(OrdersCollectionName);

                    return orders
                        .Find(o => o.ClientOrderId == clientOrderId && o.OrderId == Guid.Empty)
                        .OrderByDescending(o => o.Id);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting pending orders");

                return Enumerable.Empty<Order>();
            }
        }

        private IEnumerable<Order> GetOrders(Guid orderId)
        {
            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var orders = db.GetCollection<Order>(OrdersCollectionName);

                    return orders
                        .Find(o => o.OrderId == orderId)
                        .OrderByDescending(o => o.Id);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting orders");

                return Enumerable.Empty<Order>();
            }
        }

        private bool CheckOrder(Order order)
        {
            if (order.Status == OrderStatus.Unknown || order.Status == OrderStatus.Pending)
            {
                var pendingOrders = GetPendingOrders(order.ClientOrderId);

                if (pendingOrders.Any())
                {
                    Log.Error(messageTemplate: "Order already pending");

                    return false;
                }
            }
            else if (order.Status == OrderStatus.Placed || order.Status == OrderStatus.Rejected)
            {
                var pendingOrders = GetPendingOrders(order.ClientOrderId)
                    .ToList();

                if (!pendingOrders.Any())
                {
                    order.IsApproved = false;

                    // probably a different device order
                    Log.Information(
                        messageTemplate: "Probably order from another device: {@order}",
                        propertyValue: order.ToString());
                }
                else
                {
                    var pendingOrder = pendingOrders.First();

                    if (pendingOrder.Status == OrderStatus.Rejected)
                    {
                        Log.Error(messageTemplate: "Order already rejected");

                        return false;
                    }
                    else if (!order.IsContinuationOf(pendingOrder))
                    {
                        Log.Error(
                            messageTemplate:
                            "Order is not continuation of saved pending order! Order: {@order}, pending order: {@pendingOrder}",
                            propertyValue0: order.ToString(),
                            propertyValue1: pendingOrder.ToString());

                        return false;
                    }         
                }
            }
            else
            {
                var orders = GetOrders(order.OrderId)
                    .ToList();

                if (!orders.Any())
                {
                    Log.Error(
                        messageTemplate: "Order is not continuation of saved orders! Order: {@order}",
                        propertyValue: order.ToString());

                    return false;
                }

                var actualOrder = orders.First();

                if (!order.IsContinuationOf(actualOrder))
                {
                    Log.Error(messageTemplate: "Order is not continuation of saved order! Order: {@order}, saved order: {@actualOrder}",
                        propertyValue0: order.ToString(),
                        propertyValue1: actualOrder.ToString());

                    return false;
                }
            }

            return true;
        }
    }
}