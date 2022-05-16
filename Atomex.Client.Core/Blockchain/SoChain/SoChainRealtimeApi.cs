using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Serilog;
using System.Threading.Tasks;
using PusherClient;


namespace Atomex.Blockchain.SoChain
{
    internal record NetAddress(string Network, string Address);
    internal record Subscription(Channel Channel, IList<Action<string>> Handlers);


    public class SoChainRealtimeApi : ISoChainRealtimeApi
    {
        public string HostUrl { get; }

        public event EventHandler Connected;
        public event EventHandler Reconnecting;
        public event EventHandler Disconnected;

        private bool _isStarted;
        private Pusher _pusher;
        private readonly ConcurrentDictionary<NetAddress, Subscription> _subscriptions = new();
        private readonly Func<NetAddress, Subscription> _willNotBeCalled = _ => null;


        private readonly ILogger _log;

        public SoChainRealtimeApi(string hostUrl, ILogger log)
        {
            HostUrl = hostUrl ?? throw new ArgumentNullException(nameof(hostUrl));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task StartAsync()
        {
            if (_isStarted)
            {
                _log.Warning("Trying to start SoChainRealtimeApi while it was already started");
                return;
            }

            try
            {
                _isStarted = true;
                _pusher = new Pusher("e9f5cc20074501ca7395", new PusherOptions()
                {
                    Host = HostUrl,
                    Encrypted = true,
                });

                _pusher.ConnectionStateChanged += ConnectionStateChangedHandler;
                _pusher.Error += ErrorHandler;
                _pusher.Connected += ConnectedHandler;

                await _pusher.ConnectAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log.Error(e, "SoChainRealtimeApi failed to start");
                _isStarted = false;
            }
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

        private async void ConnectedHandler(object sender)
        {
            try
            {
                foreach (var pair in _subscriptions.Keys)
                {
                    var chanelName = $"address_{pair.Network}_{pair.Address}";
                    var _channel = await _pusher.SubscribeAsync(chanelName);
                    _channel.Bind("balance_update", OnBalanceUpdated);
                    
                    _subscriptions.AddOrUpdate(
                        pair,
                        (_) => new Subscription(_channel, new List<Action<string>>()),
                        (_, sub) => sub with { Channel = _channel });
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "SoChainRealtimeApi error while subscribing on channels");
            }
        }

        private void OnBalanceUpdated(PusherEvent @event)
        {
            _log.Debug("[BalanceUpdated] SoChainRealtimeApi got {@Event}", @event);
        }

        private void ErrorHandler(object sender, PusherException error)
        {
            if (error is ChannelDecryptionException exception)
            {
                _log.Error("SoChain Realtime API channel decryption error: {@ErrorMsg}", exception);
                return;
            }

            _log.Error("SoChain Realtime API error: {@Error}", error);
        }

        private void ConnectionStateChangedHandler(object sender, ConnectionState state)
        {
            _log.Debug("SoChainRealtimeApi connection state changed to {State}", state);

            switch (state)
            {
                case ConnectionState.Connected:
                    Connected?.Invoke(this, EventArgs.Empty);
                    break;

                case ConnectionState.Disconnected:
                    Disconnected?.Invoke(this, EventArgs.Empty);
                    break;

                case ConnectionState.WaitingToReconnect:
                    Reconnecting?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
    }
}
