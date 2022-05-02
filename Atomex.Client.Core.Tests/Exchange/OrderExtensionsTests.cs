using System;
using System.Collections.Generic;
using Atomex.Common;
using Atomex.Core;
using Xunit;

namespace Atomex.Client.Core.Tests
{
    public class OrderExtensionsTests
    {
        private static Order CreateOrder(OrderStatus status, decimal leaveQty = 0)
        {
            return new Order
            {
                LeaveQty = leaveQty,
                Status = status,
                FromWallets = new List<WalletAddress_OLD> {
                    new WalletAddress_OLD { Address = "1234" }
                }
            };
        }

        [Fact]
        public void IsContinuationOfPendingTest()
        {
            var order = CreateOrder(OrderStatus.Pending);

            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Pending)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Placed)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Canceled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.PartiallyFilled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Filled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Rejected)));
        }

        [Fact]
        public void IsContinuationOfPlacedTest()
        {
            var order = CreateOrder(OrderStatus.Placed);

            Assert.True(order.IsContinuationOf(CreateOrder(OrderStatus.Pending)));

            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Placed)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Canceled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.PartiallyFilled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Filled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Rejected)));
        }

        [Fact]
        public void IsContinuationOfCanceledTest()
        {
            var order = CreateOrder(OrderStatus.Canceled);

            Assert.True(order.IsContinuationOf(CreateOrder(OrderStatus.Placed)));
            Assert.True(order.IsContinuationOf(CreateOrder(OrderStatus.PartiallyFilled)));

            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Pending)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Canceled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Filled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Rejected)));
        }

        [Fact]
        public void IsContinuationOfFilledTest()
        {
            foreach (var status in new[] {OrderStatus.PartiallyFilled, OrderStatus.Filled})
            {
                var order = CreateOrder(status, leaveQty: 10);

                Assert.True(order.IsContinuationOf(CreateOrder(OrderStatus.Placed, leaveQty: 11)));
                Assert.True(order.IsContinuationOf(CreateOrder(OrderStatus.PartiallyFilled, leaveQty: 11)));

                Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Placed, leaveQty: 9)));
                Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.PartiallyFilled, leaveQty: 9)));
                Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Placed, leaveQty: 10)));
                Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.PartiallyFilled, leaveQty: 10)));
                Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Pending)));
                Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Canceled)));
                Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Filled)));
                Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Rejected)));
            }
        }

        [Fact]
        public void IsContinuationOfRejectedTest()
        {
            var order = CreateOrder(OrderStatus.Rejected);

            Assert.True(order.IsContinuationOf(CreateOrder(OrderStatus.Pending)));

            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Placed)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.PartiallyFilled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Canceled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Filled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Rejected)));
        }

        [Fact]
        public void IsContinuationOfThrowsTest()
        {
            var order = CreateOrder(OrderStatus.Pending);

            Assert.Throws<ArgumentNullException>(() => order.IsContinuationOf(null));
        }
    }
}