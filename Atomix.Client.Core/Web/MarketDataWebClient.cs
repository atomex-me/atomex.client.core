using System;
using System.Collections.Generic;
using System.IO;
using Atomix.Api.Proto;
using Atomix.Common.Proto;
using Atomix.Core;
using Atomix.MarketData;
using Microsoft.Extensions.Configuration;

namespace Atomix.Web
{
    public class MarketDataWebClient : BinaryWebSocketClient
    {
        public event EventHandler<QuotesEventArgs> QuotesReceived; 
        public event EventHandler<EntriesEventArgs> EntriesReceived;
        public event EventHandler<SnapshotEventArgs> SnapshotReceived; 
        public event EventHandler<OrderLogEventArgs> OrderLogReceived;

        public MarketDataWebClient(IConfiguration configuration)
            : this(configuration["MarketData:Url"])
        {
        }

        public MarketDataWebClient(string url)
            : base(url)
        {

            AddHandler(QuotesScheme.MessageId, OnQuotesHandler);
            AddHandler(EntriesScheme.MessageId, OnEntriesHandler);
            AddHandler(SnapshotScheme.MessageId, OnSnapshotHandler);
            AddHandler(OrderLogScheme.MessageId, OnOrderLogHandler);
        }

        private void OnQuotesHandler(MemoryStream stream)
        {
            var quotes = stream.Deserialize<List<Quote>>(ProtoScheme.Quotes);
            QuotesReceived?.Invoke(this, new QuotesEventArgs(quotes));
        }

        protected void OnEntriesHandler(MemoryStream stream)
        {
            var entries = stream.Deserialize<List<Entry>>(ProtoScheme.Entries);
            EntriesReceived?.Invoke(this, new EntriesEventArgs(entries));
        }

        private void OnSnapshotHandler(MemoryStream stream)
        {
            var snapshot = stream.Deserialize<Snapshot>(ProtoScheme.Snapshot);
            SnapshotReceived?.Invoke(this, new SnapshotEventArgs(snapshot));
        }

        protected void OnOrderLogHandler(MemoryStream stream)
        {
            var orders = stream.Deserialize<List<AnonymousOrder>>(ProtoScheme.OrderLog);
            OrderLogReceived?.Invoke(this, new OrderLogEventArgs(orders));
        }

        public void AuthAsync(Auth auth)
        {
            SendAsync(ProtoScheme.Auth.SerializeWithMessageId(auth));
        }

        public void SubscribeAsync(IList<Subscription> subscriptions)
        {
            SendAsync(ProtoScheme.Subscribe.SerializeWithMessageId(subscriptions));
        }

        public void UnsubscribeAsync(IList<Subscription> subscriptions)
        {
            SendAsync(ProtoScheme.Unsubscribe.SerializeWithMessageId(subscriptions));
        }
    }
}