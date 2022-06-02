using System;
using System.Collections.Generic;


namespace Atomex.Blockchain.Ethereum
{
    public interface IEthereumNotifier
    {
        string BaseUrl { get; }

        void Start();
        void Stop();


        void SubscribeOnBalanceUpdate(string address, Action<string> handler);
        void SubscribeOnBalanceUpdate(IEnumerable<string> addresses, Action<string> handler);
    }
}
