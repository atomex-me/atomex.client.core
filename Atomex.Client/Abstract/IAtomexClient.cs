using System;
using System.Threading.Tasks;

using Atomex.Client.Common;
using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;
using Atomex.Common;

namespace Atomex.Client.Abstract
{
    public interface IAtomexClient : ISwapClient
    {
        event EventHandler<ServiceEventArgs> ServiceStatusChanged;
        event EventHandler<ServiceErrorEventArgs> Error;
        event EventHandler<OrderEventArgs> OrderReceived;
        event EventHandler<SwapEventArgs> SwapReceived;
        event EventHandler<MarketDataEventArgs> QuotesUpdated;

        bool IsServiceConnected(Service service);
        Task StartAsync();
        Task StopAsync();
        void OrderSendAsync(Order order);
        void OrderCancelAsync(long id, string symbol, Side side);
        void SubscribeToMarketData(SubscriptionType type);
    }
}