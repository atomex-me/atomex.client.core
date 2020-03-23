using System;
using System.IO;
using Atomex.Api;
using Atomex.Api.Proto;
using Atomex.Core;
using Atomex.Swaps;
using Atomex.Swaps.Abstract;
using Microsoft.Extensions.Configuration;

namespace Atomex.Web
{
    public class ExchangeWebClient : BinaryWebSocketClient, ISwapClient
    {
        private const string ExchangeUrlKey = "Exchange:Url";

        public event EventHandler<OrderEventArgs> OrderReceived;
        public event EventHandler<SwapEventArgs> SwapReceived;

        public ExchangeWebClient(IConfiguration configuration, ProtoSchemes schemes)
            : this(configuration[ExchangeUrlKey], schemes)
        {
        }

        private ExchangeWebClient(string url, ProtoSchemes schemes)
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

        public void AuthAsync(Auth auth)
        {
            SendAsync(Schemes.Auth.SerializeWithMessageId(auth));
        }

        public void OrderSendAsync(Order order)
        {
            SendAsync(Schemes.OrderSend.SerializeWithMessageId(order));
        }

        public void OrderCancelAsync(Order order)
        {
            SendAsync(Schemes.OrderCancel.SerializeWithMessageId(order));
        }

        public void OrderStatusAsync(Request<Order> request)
        {
            SendAsync(Schemes.OrderStatus.SerializeWithMessageId(request));
        }

        public void OrdersAsync(Request<Order> request)
        {
            SendAsync(Schemes.Orders.SerializeWithMessageId(request));
        }

        public void SwapInitiateAsync(Swap swap)
        {
            SendAsync(Schemes.SwapInitiate.SerializeWithMessageId(swap));
        }

        public void SwapAcceptAsync(Swap swap)
        {
            SendAsync(Schemes.SwapAccept.SerializeWithMessageId(swap));
        }

        public void SwapPaymentAsync(Swap swap)
        {
            SendAsync(Schemes.SwapPayment.SerializeWithMessageId(swap));
        }
    }
}