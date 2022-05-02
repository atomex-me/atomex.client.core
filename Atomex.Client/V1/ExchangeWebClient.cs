using System;
using System.IO;

using Atomex.Client.V1.Proto;
using Atomex.Client.V1.Entities;
using Atomex.Client.V1.Common;
using Atomex.Client.Abstract;
using Atomex.Common;

namespace Atomex.Client.V1
{
    public class ExchangeWebClient : BinaryWebSocketClient, ISwapClient
    {
        public event EventHandler<OrderEventArgs> OrderReceived;
        public event EventHandler<SwapEventArgs> SwapReceived;

        public ExchangeWebClient(string url, ProtoSchemes schemes)
            : base(url, schemes)
        {
            AddHandler(Schemes.Order.MessageId, OnOrderHandler);
            AddHandler(Schemes.Swap.MessageId, OnSwapHandler);
        }

        private void OnOrderHandler(MemoryStream stream)
        {
            var response = Schemes.Order.DeserializeWithLengthPrefix(stream);

            OrderReceived?.Invoke(this, new OrderEventArgs(response.Data));
        }

        private void OnSwapHandler(MemoryStream stream)
        {
            var response = Schemes.Swap.DeserializeWithLengthPrefix(stream);

            SwapReceived?.Invoke(this, new SwapEventArgs(response.Data));
        }

        public void AuthAsync(Auth auth) =>
            SendAsync(Schemes.Auth.SerializeWithMessageId(auth));

        public void OrderSendAsync(Order order) =>
            SendAsync(Schemes.OrderSend.SerializeWithMessageId(order));

        public void OrderCancelAsync(long id, string symbol, Side side) =>
            SendAsync(Schemes.OrderCancel.SerializeWithMessageId(new Order
            {
                Id     = id,
                Symbol = symbol,
                Side   = side
            }));

        public void OrderStatusAsync(Request<Order> request) =>
            SendAsync(Schemes.OrderStatus.SerializeWithMessageId(request));

        public void OrdersAsync(Request<Order> request) =>
            SendAsync(Schemes.Orders.SerializeWithMessageId(request));

        public void SwapInitiateAsync(
            long id,
            byte[] secretHash,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress) =>
            SendAsync(Schemes.SwapInitiate.SerializeWithMessageId(new Swap
            {
                Id              = id,
                SecretHash      = secretHash,
                Symbol          = symbol,
                ToAddress       = toAddress,
                RewardForRedeem = rewardForRedeem,
                RefundAddress   = refundAddress
            }));

        public void SwapAcceptAsync(
            long id,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress) =>
            SendAsync(Schemes.SwapAccept.SerializeWithMessageId(new Swap
            {
                Id              = id,
                Symbol          = symbol,
                ToAddress       = toAddress,
                RewardForRedeem = rewardForRedeem,
                RefundAddress   = refundAddress
            }));

        public void SwapStatusAsync(
            string requestId,
            long swapId) =>
            SendAsync(Schemes.SwapStatus.SerializeWithMessageId(new Request<Swap>
            {
                Id   = requestId,
                Data = new Swap { Id = swapId }
            }));
    }
}