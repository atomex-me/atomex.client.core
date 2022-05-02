﻿using System;
using System.Collections.Generic;
using System.IO;

using Atomex.Client.V1.Proto;
using Atomex.Client.V1.Entities;
using Atomex.Client.V1.Common;
using Atomex.MarketData.Common;

namespace Atomex.Client.V1
{
    public class MarketDataWebClient : BinaryWebSocketClient
    {
        public event EventHandler<QuotesEventArgs> QuotesReceived;
        public event EventHandler<EntriesEventArgs> EntriesReceived;
        public event EventHandler<SnapshotEventArgs> SnapshotReceived;
        public event EventHandler<OrderLogEventArgs> OrderLogReceived;

        public MarketDataWebClient(string url, ProtoSchemes schemes)
            : base(url, schemes)
        {
            AddHandler(Schemes.Quotes.MessageId, OnQuotesHandler);
            AddHandler(Schemes.Entries.MessageId, OnEntriesHandler);
            AddHandler(Schemes.Snapshot.MessageId, OnSnapshotHandler);
            AddHandler(Schemes.OrderLog.MessageId, OnOrderLogHandler);
        }

        private void OnQuotesHandler(MemoryStream stream)
        {
            var quotes = Schemes.Quotes.DeserializeWithLengthPrefix(stream);

            QuotesReceived?.Invoke(this, new QuotesEventArgs(quotes));
        }

        private void OnEntriesHandler(MemoryStream stream)
        {
            var entries = Schemes.Entries.DeserializeWithLengthPrefix(stream);

            EntriesReceived?.Invoke(this, new EntriesEventArgs(entries));
        }

        private void OnSnapshotHandler(MemoryStream stream)
        {
            var snapshot = Schemes.Snapshot.DeserializeWithLengthPrefix(stream);

            SnapshotReceived?.Invoke(this, new SnapshotEventArgs(snapshot));
        }

        private void OnOrderLogHandler(MemoryStream stream)
        {
            var orders = Schemes.OrderLog.DeserializeWithLengthPrefix(stream);

            OrderLogReceived?.Invoke(this, new OrderLogEventArgs(orders));
        }

        public void AuthAsync(Auth auth) =>
            SendAsync(Schemes.Auth.SerializeWithMessageId(auth));

        public void SubscribeAsync(List<Subscription> subscriptions) =>
            SendAsync(Schemes.Subscribe.SerializeWithMessageId(subscriptions));

        public void UnsubscribeAsync(List<Subscription> subscriptions) =>
            SendAsync(Schemes.Unsubscribe.SerializeWithMessageId(subscriptions));
    }
}