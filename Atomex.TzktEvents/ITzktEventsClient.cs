using System;
using System.Threading.Tasks; 

namespace Atomex.TzktEvents
{
    public interface ITzktEventsClient
    {
        string BaseUri { get; }

        string EventsUrl { get; }

        event EventHandler Connected;
        event EventHandler Disconnected;

        Task StartAsync(string baseUri);
        Task StopAsync();


        Task NotifyOnAccountAsync(string address, Action handler);
    }
}
