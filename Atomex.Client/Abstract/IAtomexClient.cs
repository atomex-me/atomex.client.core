using System;
using System.Threading.Tasks;

using Atomex.Client.Common;
using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;
using Atomex.Common;
using Atomex.MarketData.Common;

namespace Atomex.Client.Abstract
{
    public interface IAtomexClient : ISwapClient
    {
        event EventHandler<ServiceEventArgs> ServiceStatusChanged;
        event EventHandler<ServiceErrorEventArgs> Error;
        event EventHandler<OrderEventArgs> OrderUpdated;
        event EventHandler<SwapEventArgs> SwapUpdated;
        event EventHandler<QuotesEventArgs> QuotesUpdated;
        event EventHandler<EntriesEventArgs> EntriesUpdated;
        event EventHandler<SnapshotEventArgs> SnapshotUpdated;

        bool IsServiceConnected(Service service);
        Task StartAsync();
        Task StopAsync();
        void OrderSendAsync(Order order);
        void OrderCancelAsync(long id, string symbol, Side side);
        void SubscribeToMarketData(SubscriptionType type);
    }
}