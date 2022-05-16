using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using PusherClient;


namespace Atomex.Blockchain.SoChain
{
    public class SoChainRealtimeApi : ISoChainRealtimeApi
    {
        public string HostUrl { get; }

        public event EventHandler Connected;
        public event EventHandler Reconnecting;
        public event EventHandler Disconnected;

        private bool _isStarted;
        private Pusher _pusher;
        private readonly ConcurrentDictionary<string, Channel> _channels = new();


        public SoChainRealtimeApi(string hostUrl)
        {
            HostUrl = hostUrl;
        }

        public Task StartAsync()
        {
            throw new NotImplementedException();
        }

        public Task StopAsync()
        {
            throw new NotImplementedException();
        }

        public Task SubscribeOnBalanceUpdateAsync(string network, string address, Action<string> handler)
        {
            throw new NotImplementedException();
        }

        public Task SubscribeOnBalanceUpdateAsync(string network, IEnumerable<string> addresses, Action<string> handler)
        {
            throw new NotImplementedException();
        }

        public Task UnsubscribeOnBalanceUpdateAsync(string network, string address, Action<string> handler = null)
        {
            throw new NotImplementedException();
        }

        public Task UnsubscribeOnBalanceUpdateAsync(string network, IEnumerable<string> addresses, Action<string> handler = null)
        {
            throw new NotImplementedException();
        }
    }
}
