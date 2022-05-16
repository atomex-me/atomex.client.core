using System;
using System.Collections.Generic;
using System.Threading.Tasks;


namespace Atomex.Blockchain.SoChain
{
    public interface ISoChainRealtimeApi
    {
        string HostUrl { get; }

        event EventHandler Connected;
        event EventHandler Reconnecting;
        event EventHandler Disconnected;

        Task StartAsync();
        Task StopAsync();

        Task SubscribeOnBalanceUpdateAsync(string network, string address, Action<string> handler);
        Task SubscribeOnBalanceUpdateAsync(string network, IEnumerable<string> addresses, Action<string> handler);

        Task UnsubscribeOnBalanceUpdateAsync(string network, string address, Action<string> handler = null);
        Task UnsubscribeOnBalanceUpdateAsync(string network, IEnumerable<string> addresses, Action<string> handler = null);
    }
}
