using System;
using System.IO;
using Atomix.Api.Proto;
using Atomix.Common.Proto;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Swaps;
using Atomix.Swaps.Abstract;
using Microsoft.Extensions.Configuration;

namespace Atomix.Web
{
    public class ExchangeWebClient : BinaryWebSocketClient, ISwapClient
    {
        private const string ExchangeUrlKey = "Exchange:Url";

        public event EventHandler<ExecutionReportEventArgs> ExecutionReportReceived;
        public event EventHandler<SwapDataEventArgs> SwapDataReceived;

        public ExchangeWebClient(IConfiguration configuration)
            : this(configuration[ExchangeUrlKey])
        {
        }

        public ExchangeWebClient(string url)
            : base(url)
        {
            AddHandler(ExecutionReportScheme.MessageId, ExecutionReportHandler);
            AddHandler(SwapDataScheme.MessageId, OnSwapDataHandler);
        }

        protected void ExecutionReportHandler(MemoryStream stream)
        {
            var executionReport = stream.Deserialize<ExecutionReport>(ProtoScheme.ExecutionReport);
            ExecutionReportReceived?.Invoke(this, new ExecutionReportEventArgs(executionReport));
        }

        protected void OnSwapDataHandler(MemoryStream stream)
        {
            var swapData = stream.Deserialize<SwapData>(ProtoScheme.Swap);
            SwapDataReceived?.Invoke(this, new SwapDataEventArgs(swapData));
        }

        public void AuthAsync(Auth auth)
        {
            SendAsync(ProtoScheme.Auth.SerializeWithMessageId(auth));
        }

        public void OrderSendAsync(Order order)
        {
            SendAsync(ProtoScheme.OrderSend.SerializeWithMessageId(order));
        }

        public void OrderCancelAsync(Order order)
        {
            SendAsync(ProtoScheme.OrderCancel.SerializeWithMessageId(order));
        }

        public void OrderStatusAsync(Order order)
        {
            SendAsync(ProtoScheme.OrderStatus.SerializeWithMessageId(order));
        }

        public void OrdersAsync()
        {
            SendAsync(new byte[]{ OrdersScheme.MessageId });
        }

        public void SendSwapDataAsync(SwapData swapData)
        {
            SendAsync(ProtoScheme.Swap.SerializeWithMessageId(swapData));
        }
    }
}