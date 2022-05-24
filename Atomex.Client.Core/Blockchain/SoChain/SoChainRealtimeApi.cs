using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Serilog;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PusherClient;


namespace Atomex.Blockchain.SoChain
{
    internal record FullAddress(string Network, string Address);
    internal record Subscription(Channel Channel, ImmutableHashSet<Action<string>> Handlers);


    public class SoChainRealtimeApi : ISoChainRealtimeApi
    {
        public string HostUrl { get; }

        public event EventHandler Connected;
        public event EventHandler Reconnecting;
        public event EventHandler Disconnected;

        private bool _isStarted;
        private Pusher _pusher;

        private readonly ILogger _log;
        private readonly ConcurrentDictionary<FullAddress, Subscription> _subscriptions = new();
        private readonly ConcurrentDictionary<string, Action<PusherEvent>> _balanceUpdatedHandlers = new();

        public SoChainRealtimeApi(string hostUrl, ILogger log)
        {
            HostUrl = hostUrl ?? throw new ArgumentNullException(nameof(hostUrl));
            _log = log?.ForContext<SoChainRealtimeApi>() ?? throw new ArgumentNullException(nameof(log));
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
                _pusher = new Pusher("e9f5cc20074501ca7395", new PusherOptions
                {
                    Host = HostUrl,
                    Encrypted = true,
                });

                _pusher.ConnectionStateChanged += ConnectionStateChangedHandler;
                _pusher.Error += ErrorHandler;
                _pusher.Connected += ConnectedHandler;

                await _pusher.ConnectAsync().ConfigureAwait(false);

                _log.Information("SoChainRealtimeApi successfully started");
            }
            catch (Exception e)
            {
                _log.Error(e, "SoChainRealtimeApi failed to start");
                _isStarted = false;
            }
        }

        public async Task StopAsync()
        {
            if (!_isStarted)
            {
                _log.Warning("SoChainRealtimeApi was not started");
                return;
            }

            try
            {
                _pusher.ConnectionStateChanged -= ConnectionStateChangedHandler;
                _pusher.Error -= ErrorHandler;
                _pusher.Connected -= ConnectedHandler;

                await _pusher.DisconnectAsync().ConfigureAwait(false);
                _subscriptions.Clear();

                _log.Information("SoChainRealtimeApi successfully stopped");
            }
            catch (Exception e)
            {
                _log.Error(e, "SoChainRealtimeApi was stopped with error");
            }
            finally
            {
                _isStarted = false;
            }
        }

        public Task SubscribeOnBalanceUpdateAsync(string network, string address, Action<string> handler)
        {
            return OnBalanceUpdateAsync(network, address, handler);
        }

        public Task SubscribeOnBalanceUpdateAsync(string network, IEnumerable<string> addresses, Action<string> handler)
        {
            var subscribeTasks = addresses.Select(address => OnBalanceUpdateAsync(network, address, handler));
            return Task.WhenAll(subscribeTasks);
        }

        public void UnsubscribeOnBalanceUpdate(string network, string address, Action<string> handler = null)
        {
            try
            {
                var fullAddress = new FullAddress(network, address);
                if (_subscriptions.TryGetValue(fullAddress, out var subscription))
                {
                    if (handler == null)
                    {
                        subscription.Channel.UnbindAll();
                        var removed = _subscriptions.TryRemove(fullAddress, out _);

                        if (!removed)
                        {
                            _log.Warning("SoChainRealtimeApi couldn't remove subscription for {@Address} at {@Network}", address, network);
                        }
                    }
                    else
                    {
                        var updated = _subscriptions.TryUpdate(fullAddress,
                            subscription with {Handlers = subscription.Handlers.Remove(handler)}, subscription);

                        if (!updated)
                        {
                            _log.Warning("SoChainRealtimeApi couldn't remove subscription for handler on {@Address} at {@Network}", address, network);
                        }
                    }
                }
                else
                {
                    _log.Warning(
                        "SoChainRealtimeApi Unsubscribe was called with unregistered {@Address} on {@Network} network",
                        address, network);
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "SoChainRealtimeApi error while unsubscribing {@Address} on {@Network} network",
                    address, network);
            }
        }

        public void UnsubscribeOnBalanceUpdate(string network, IEnumerable<string> addresses,
            Action<string> handler = null)
        {
            foreach (var address in addresses)
            {
                UnsubscribeOnBalanceUpdate(network, address, handler);
            }
        }

        private async Task OnBalanceUpdateAsync(string network, string address, Action<string> handler)
        {
            try
            {
                var fullAddress = new FullAddress(network, address);
                if (_subscriptions.TryGetValue(fullAddress, out var subscription))
                {
                    var updated = _subscriptions.TryUpdate(fullAddress,
                        subscription with {Handlers = subscription.Handlers.Add(handler)}, subscription);

                    if (!updated)
                    {
                        _log.Warning(
                            "SoChainRealtimeApi couldn't update subscription with new handler for {@Address} on {@Network} network",
                            address, network);
                    }

                    return;
                }

                var chanelName = $"address_{network}_{address}";
                var channel = await _pusher.SubscribeAsync(chanelName);
                var balanceUpdatedHandler = CreateOrGetBalanceUpdatedHandler(network);
                channel.Bind("balance_update", balanceUpdatedHandler);

                var handlers = ImmutableHashSet.Create(handler);
                var added = _subscriptions.TryAdd(fullAddress, new Subscription(channel, handlers));

                if (!added)
                {
                    _log.Warning(
                        "SoChainRealtimeApi couldn't add new subscription for {@Address} on {@Network} network",
                        address, network);
                }
            }
            catch (Exception e)
            {
                _log.Error(e,
                    "SoChainRealtimeApi error while subscribing on balance update for {Address} on {@Network}",
                    address, network);
            }
        }

        private async void ConnectedHandler(object sender)
        {
            try
            {
                foreach (var fullAddress in _subscriptions.Select(sub => sub.Key))
                {
                    var chanelName = $"address_{fullAddress.Network}_{fullAddress.Address}";
                    var channel = await _pusher.SubscribeAsync(chanelName).ConfigureAwait(false);
                    var balanceUpdatedHandler = CreateOrGetBalanceUpdatedHandler(fullAddress.Network);
                    channel.Bind("balance_update", balanceUpdatedHandler);
                
                    _subscriptions.AddOrUpdate(
                        fullAddress,
                        (_) => new Subscription(channel, ImmutableHashSet.Create<Action<string>>()),
                        (_, sub) => sub with {Channel = channel});
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "SoChainRealtimeApi error while subscribing on channels");
            }
        }

        private Action<PusherEvent> CreateOrGetBalanceUpdatedHandler(string network)
        {
            if (_balanceUpdatedHandlers.TryGetValue(network, out var balanceUpdatedHandler))
            {
                return balanceUpdatedHandler;
            }

            balanceUpdatedHandler = (@event) =>
            {
                _log.Debug("[BalanceUpdated] SoChainRealtimeApi got {@Event}", @event);

                if (@event.EventName != "balance_update")
                {
                    _log.Warning("SoChainRealtimeApi OnBalanceUpdated got event with unsupported name {@EventName}",
                        @event.EventName);
                    return;
                }

                try
                {
                    var data = JObject.Parse(@event.Data);
                    var address = data["value"]?["address"]?.ToString();
                    
                    var fullAddress = new FullAddress(network, address);
                    if (_subscriptions.TryGetValue(fullAddress, out var subscription))
                    {
                        foreach (var handler in subscription.Handlers)
                        {
                            handler(address);
                        }
                    }
                }
                catch (Exception e)
                {
                    _log.Error(e, "SoChainRealtimeApi error on handling balance update event");
                }
            };

            return _balanceUpdatedHandlers.GetOrAdd(network, balanceUpdatedHandler);
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
