using System;
using System.Threading.Tasks; 

namespace Atomex.TzktEvents
{
    public interface ITzktEventsClient
    {
        string BaseUri { get; }

        Task StartAsync(string baseUri);
        Task StopAsync();

        Task NotifyOnBalanceChange(string address, Action handler);
    }
}
