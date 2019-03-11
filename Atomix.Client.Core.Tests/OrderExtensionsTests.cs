using System;
using System.Collections.Generic;
using Atomix.Common;
using Atomix.Core;
using Atomix.Core.Entities;
using Xunit;

namespace Atomix.Client.Core.Tests
{
    public class OrderExtensionsTests
    {
        private static Order CreateOrder(OrderStatus status, decimal leaveQty = 0)
        {
            return new Order
            {
                LeaveQty = leaveQty,
                Status = status,
                FromWallets = new List<WalletAddress> {
                    new WalletAddress { Address = "1234" }
                },
                ToWallet = new WalletAddress { Address = "4321" },
                RefundWallet = new WalletAddress { Address = "5678" }
            };
        }

        [Fact]
        public void IsContinuationOfUnknownTest()
        {
            var order = CreateOrder(OrderStatus.Unknown);

            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Unknown)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Pending)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Placed)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Canceled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.PartiallyFilled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Filled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Rejected)));
        }

        [Fact]
        public void IsContinuationOfPendingTest()
        {
            var order = CreateOrder(OrderStatus.Pending);

            Assert.True(order.IsContinuationOf(CreateOrder(OrderStatus.Unknown)));

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

            Assert.True(order.IsContinuationOf(CreateOrder(OrderStatus.Unknown)));
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

            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Unknown)));
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
                Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Unknown)));
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

            Assert.True(order.IsContinuationOf(CreateOrder(OrderStatus.Unknown)));
            Assert.True(order.IsContinuationOf(CreateOrder(OrderStatus.Placed)));

            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.PartiallyFilled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Pending)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Canceled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Filled)));
            Assert.False(order.IsContinuationOf(CreateOrder(OrderStatus.Rejected)));
        }

        [Fact]
        public void IsContinuationOfThrowsTest()
        {
            var order = CreateOrder(OrderStatus.Unknown);

            Assert.Throws<ArgumentNullException>(() => order.IsContinuationOf(null));
        }
    }
}