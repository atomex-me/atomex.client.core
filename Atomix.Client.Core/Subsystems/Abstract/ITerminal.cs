using System;
using System.Threading.Tasks;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.MarketData;
using Atomix.Swaps;
using Atomix.Wallet.Abstract;

namespace Atomix.Subsystems.Abstract
{
    public interface ITerminal
    {
        event EventHandler<TerminalServiceEventArgs> ServiceConnected;
        event EventHandler<TerminalServiceEventArgs> ServiceDisconnected;
        event EventHandler<MarketDataEventArgs> QuotesUpdated;
        //event EventHandler<MarketDataEventArgs> OrderBookUpdated;
        event EventHandler<ExecutionReportEventArgs> ExecutionReportReceived;
        event EventHandler<SwapEventArgs> SwapUpdated;
        event EventHandler<TerminalErrorEventArgs> Error;

        bool IsServiceConnected(TerminalService service);
        Task StartAsync();
        Task StopAsync();
        void OrderSendAsync(Order order);
        void OrderCancelAsync(Order order);
        void SubscribeToMarketData(SubscriptionType type);
        MarketDataOrderBook GetOrderBook(Symbol symbol);
        Quote GetQuote(Symbol symbol);

        Task<ITerminal> ChangeAccountAsync(IAccount account, bool restart = true);
    }
}