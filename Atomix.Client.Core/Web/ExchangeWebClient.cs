using System;
using System.IO;
using Atomix.Api.Proto;
using Atomix.Common.Proto;
using Atomix.Core;
using Atomix.Core.Entities;
using Microsoft.Extensions.Configuration;

namespace Atomix.Web
{
    public class ExchangeWebClient : BinaryWebSocketClient
    {
        private const string ExchangeUrlKey = "Exchange:Url";

        public event EventHandler<ExecutionReportEventArgs> ExecutionReportReceived;

        public ExchangeWebClient(IConfiguration configuration)
            : this(configuration[ExchangeUrlKey])
        {
        }

        public ExchangeWebClient(string url)
            : base(url)
        {
            AddHandler(ExecutionReportScheme.MessageId, ExecutionReportHandler);
        }

        protected void ExecutionReportHandler(MemoryStream stream)
        {
            var executionReport = stream.Deserialize<ExecutionReport>(ProtoScheme.ExecutionReport);
            ExecutionReportReceived?.Invoke(this, new ExecutionReportEventArgs(executionReport));
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
    }
}