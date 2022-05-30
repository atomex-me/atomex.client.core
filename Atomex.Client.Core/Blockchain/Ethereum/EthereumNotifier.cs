using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Collections.Generic;
using Atomex.Web;
using Serilog;


namespace Atomex.Blockchain.Ethereum
{
    public class EthereumNotifier : IEthereumNotifier
    {
        public string BaseUrl { get; }
        private readonly string _eventsWs;
        private readonly ILogger _log;

        private WebSocketClient _events;
        private bool _isConnected;

        private readonly ConcurrentDictionary<string, Action<string>> _actions = new();

        public EthereumNotifier(string baseUrl, string eventsWs, ILogger log)
        {
            BaseUrl = baseUrl;
            _eventsWs = eventsWs;
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task StartAsync()
        {
            if (_isConnected)
            {
                return;
            }

            try
            {
                _isConnected = true;

                _events = new WebSocketClient(_eventsWs);
                await _events.ConnectAsync();
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on starting EthereumNotifier");
            }
        }

        public async Task StopAsync()
        {
            if (!_isConnected)
            {
                return;
            }

            try
            {
                await _events.CloseAsync();
                _actions.Clear();
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on stopping EthereumNotifier");

            }
            finally
            {
                _isConnected = false;
            }
        }

        public Task SubscribeOnBalanceUpdate(string address, Action<string> handler)
        {
            _actions.AddOrUpdate(address, handler, (_, _) => handler);
            return Task.CompletedTask;
        }

        public Task SubscribeOnBalanceUpdate(IEnumerable<string> addresses, Action<string> handler)
        {
            foreach (var address in addresses)
            {
                _actions.AddOrUpdate(address, handler, (_, _) => handler);
            }

            return Task.CompletedTask;
        }
    }
}
