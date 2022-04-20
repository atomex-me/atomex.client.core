using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;

using Atomex.TzktEvents.Services;
using Atomex.TzktEvents.Models;
using Newtonsoft.Json.Linq;


namespace Atomex.TzktEvents
{
    public class TzktEventsClient : ITzktEventsClient
    {
        public string BaseUri { get; private set; }
        public string EventsUrl => $"{BaseUri}/events";

        private HubConnection _connection;
        private bool _isStarted;

        private readonly IHubConnectionCreator _hubConnectionCreator;
        private readonly ILogger _log;

        private readonly ConcurrentDictionary<string, Action> _accountHandlers = new();

        public TzktEventsClient(IHubConnectionCreator hubConnectionCreator, ILogger log)
        {
            _hubConnectionCreator = hubConnectionCreator ?? throw new ArgumentNullException(nameof(hubConnectionCreator));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        private async Task InitAsync(Exception? exception = null)
        {
            if (exception != null)
            {
                _log.Warning($"Connection closed due to an error: {exception}. Reconnecting.");
            }

            await _connection.StartAsync();
            _log.Debug($"Established connection to TzKT events with id: {_connection.ConnectionId}.");


            if (!_accountHandlers.IsEmpty)
            {
                var addresses = _accountHandlers.Keys.ToArray();
                await _connection.InvokeAsync(SubscriptionMethod.SubscribeToAccounts.Method, new
                {
                    addresses
                });
            }
        }

        public async Task StartAsync(string baseUri)
        {
            if (_isStarted)
            {
                _log.Warning($"Trying to start new connection with baseUri = {baseUri} while TzktEventsClient is already connected to {EventsUrl}.");
                return;
            }

            _isStarted = true;
            BaseUri = baseUri;

            _connection = _hubConnectionCreator.Create(EventsUrl);
            _connection.Closed += InitAsync;

            // TODO: Move to Set subscriptions method
            _connection.On(SubscriptionMethod.SubscribeToAccounts.Channel, (JObject msg) =>
            {
                _log.Debug($"Has got msg from TzktEvents on 'operations' channel: {msg}.");
                Console.WriteLine(msg);

                if (msg["type"]?.Value<int>() == 1)
                {
                    foreach (var account in msg["data"])
                    {
                        var address = account["address"]?.ToString();
                        if (address != null && _accountHandlers.TryGetValue(address, out var addressHandler))
                        {
                            addressHandler();
                        }
                    }
                }

            });

            await InitAsync();
        }

        public async Task StopAsync()
        {
            if (!_isStarted)
            {
                _log.Warning("Connection of TzktEventsClient was not started.");
                return;
            }

            _connection.Closed -= InitAsync;
            await _connection.StopAsync();
            await _connection.DisposeAsync();
            _isStarted = false;
        }

        public async Task NotifyOnAccount(string address, Action handler)
        {
            await _connection.InvokeAsync(SubscriptionMethod.SubscribeToAccounts.Method, new
            {
                addresses = new []{ address }
            });

            _accountHandlers.AddOrUpdate(address, handler, (_, _) => handler);
        }
    }
}
