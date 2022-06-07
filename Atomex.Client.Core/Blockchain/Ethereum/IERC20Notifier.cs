using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Atomex.EthereumTokens;


namespace Atomex.Blockchain.Ethereum
{
    public interface IERC20Notifier
    {
        string BaseUrl { get; }
        Erc20Config Currency { get; }

        Task StartAsync();
        Task StopAsync();


        Task SubscribeOnEventsAsync(string address, Action<string, string> handler);
        Task SubscribeOnEventsAsync(IEnumerable<string> addresses, Action<string, string> handler);
    }
}
