using System;
using System.Threading.Tasks;

using Atomex.Core;
using Atomex.MarketData;
using Atomex.MarketData.Abstract;
using Atomex.Swaps;
using Atomex.Swaps.Abstract;
using Atomex.Wallet.Abstract;

namespace Atomex.Services.Abstract
{
    public interface IAtomexClient : ISwapClient
    {
        event EventHandler<AtomexClientServiceEventArgs> ServiceConnected;
        event EventHandler<AtomexClientServiceEventArgs> ServiceDisconnected;
        event EventHandler<AtomexClientServiceEventArgs> ServiceAuthenticated;
        event EventHandler<AtomexClientErrorEventArgs> Error;
        event EventHandler<OrderEventArgs> OrderUpdated;
        event EventHandler<SwapEventArgs> SwapUpdated;
        event EventHandler<MarketDataEventArgs> QuotesUpdated;

        IAccount Account { get; }
        IMarketDataRepository MarketDataRepository { get; }

        bool IsServiceConnected(AtomexClientService service);
        Task StartAsync();
        Task StopAsync();

        void OrderSendAsync(Order order);
        void OrderCancelAsync(long id, string symbol, Side side);
        void SubscribeToMarketData(SubscriptionType type);
        MarketDataOrderBook GetOrderBook(string symbol);
        MarketDataOrderBook GetOrderBook(Symbol symbol);
        Quote GetQuote(Symbol symbol);
    }
}