using System;
using System.Collections.Generic;
using System.Threading.Tasks; 

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

        Task NotifyOnTokenBalancesAsync(string address, Action<string> handler);
        Task NotifyOnTokenBalancesAsync(IEnumerable<string> address, Action<string> handler);
    }
}
