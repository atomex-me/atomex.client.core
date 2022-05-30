using System;
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
        private WebSocketClient _events;
        private readonly ILogger _log;
        private bool _isConnected;

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
            throw new NotImplementedException();
        }

        public Task SubscribeOnBalanceUpdate(IEnumerable<string> addresses, Action<string> handler)
        {
            throw new NotImplementedException();
        }
    }
}
