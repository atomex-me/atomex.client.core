using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Atomex.TzktEvents.Models;


namespace Atomex.TzktEvents
{
    public interface ITzktEventsClient
    {
        string EventsUrl { get; }

        event EventHandler Connected;
        event EventHandler Reconnecting;
        event EventHandler Disconnected;

        Task StartAsync(string baseUri);
        Task StopAsync();
        
        Task NotifyOnAccountAsync(string address, Action<string> handler);
        Task NotifyOnAccountsAsync(IEnumerable<string> addresses, Action<string> handler);

        Task NotifyOnTokenBalancesAsync(string address, Action<TezosTokenEvent> handler);
        Task NotifyOnTokenBalancesAsync(IEnumerable<string> addresses, Action<TezosTokenEvent> handler);
    }
}
