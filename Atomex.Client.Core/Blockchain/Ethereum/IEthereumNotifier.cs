using System;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace Atomex.Blockchain.Ethereum
{
    public interface IEthereumNotifier
    {
        string BaseUrl { get; }

        Task StartAsync();
        Task StopAsync();


        Task SubscribeOnBalanceUpdate(string address, Action<string> handler);
        Task SubscribeOnBalanceUpdate(IEnumerable<string> addresses, Action<string> handler);
    }
}
