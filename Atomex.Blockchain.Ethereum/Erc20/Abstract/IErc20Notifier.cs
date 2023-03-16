using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Atomex.Blockchain.Ethereum
{
    public interface IErc20Notifier
    {
        string BaseUrl { get; }
        string Currency { get; }
        string ContractAddress { get; }

        Task StartAsync();
        Task StopAsync();

        Task SubscribeOnEventsAsync(string address, Action<string, string> handler);
        Task SubscribeOnEventsAsync(IEnumerable<string> addresses, Action<string, string> handler);
    }
}
