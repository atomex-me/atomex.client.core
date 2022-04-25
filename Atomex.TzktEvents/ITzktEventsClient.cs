using System;
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


        Task NotifyOnAccountAsync(string address, Action handler);
    }
}
