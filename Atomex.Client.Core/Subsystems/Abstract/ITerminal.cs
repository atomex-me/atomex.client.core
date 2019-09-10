using System;
using System.Threading.Tasks;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.MarketData;
using Atomex.Swaps;
using Atomex.Wallet.Abstract;

namespace Atomex.Subsystems.Abstract
{
    public interface ITerminal
    {
        event EventHandler<TerminalServiceEventArgs> ServiceConnected;
        event EventHandler<TerminalServiceEventArgs> ServiceDisconnected;
        event EventHandler<TerminalServiceEventArgs> ServiceAuthenticated;
        event EventHandler<TerminalErrorEventArgs> Error;
        event EventHandler<OrderEventArgs> OrderReceived;
        event EventHandler<MarketDataEventArgs> QuotesUpdated;
        //event EventHandler<MarketDataEventArgs> OrderBookUpdated;
        event EventHandler<SwapEventArgs> SwapUpdated;

        bool IsServiceConnected(TerminalService service);
        Task StartAsync();
        Task StopAsync();
        Task ChangeAccountAsync(IAccount account, bool restart = true);

        void OrderSendAsync(Order order);
        void OrderCancelAsync(Order order);
        void SubscribeToMarketData(SubscriptionType type);
        MarketDataOrderBook GetOrderBook(Symbol symbol);
        Quote GetQuote(Symbol symbol);
    }
}