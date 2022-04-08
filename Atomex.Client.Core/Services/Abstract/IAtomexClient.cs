using System;
using System.Threading.Tasks;

using Atomex.Core;
using Atomex.MarketData;
using Atomex.Swaps;
using Atomex.Wallet.Abstract;

namespace Atomex.Services.Abstract
{
    public interface IAtomexClient
    {
        event EventHandler<AtomexClientServiceEventArgs> ServiceConnected;
        event EventHandler<AtomexClientServiceEventArgs> ServiceDisconnected;
        event EventHandler<AtomexClientServiceEventArgs> ServiceAuthenticated;
        event EventHandler<AtomexClientErrorEventArgs> Error;
        event EventHandler<OrderEventArgs> OrderReceived;
        event EventHandler<MarketDataEventArgs> QuotesUpdated;
        event EventHandler<SwapEventArgs> SwapUpdated;

        IAccount Account { get; }

        bool IsServiceConnected(AtomexClientService service);
        Task StartAsync();
        Task StopAsync();

        void OrderSendAsync(Order order);
        void OrderCancelAsync(Order order);
        void SubscribeToMarketData(SubscriptionType type);
        MarketDataOrderBook GetOrderBook(string symbol);
        MarketDataOrderBook GetOrderBook(Symbol symbol);
        Quote GetQuote(Symbol symbol);
    }
}