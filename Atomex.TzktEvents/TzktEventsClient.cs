using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Atomex.Common;
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

        private readonly ConcurrentDictionary<string, Action> _balanceChangedHandlers = new();

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



            _connection.On(SubscriptionMethod.SubscribeToHead.GetDescription(), (object msg) =>
            {
                _log.Debug($"Has got msg from TzktEvents on 'head' channel: {msg}.");
            });

            /*_connection.On(SubscriptionMethod.SubscribeToOperations.GetDescription(), (Object msg) =>
            {
                _log.Debug($"Has got msg from TzktEvents on 'operations' channel: {msg}.");
            });*/

            _connection.On(SubscriptionMethod.SubscribeToOperations.GetDescription(), (JObject msg) =>
            {
                if ((int) msg["type"] == 1)
                {
                    foreach (var operation in msg["data"])
                    {
                        if (operation["type"]?.ToString() != "transaction") continue;

                        var senderAddress = operation["sender"]?["address"]?.ToString();
                        var targetAddress = operation["target"]?["address"]?.ToString();

                        if (senderAddress != null && _balanceChangedHandlers.TryGetValue(senderAddress, out var senderHandler))
                        {
                            senderHandler();
                        }

                        if (targetAddress != null && _balanceChangedHandlers.TryGetValue(targetAddress, out var targetHandler))
                        {
                            targetHandler();
                        }
                    }
                }

                _log.Debug($"Has got msg from TzktEvents on 'operations' channel: {msg}.");
            });
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

        public async Task NotifyOnBalanceChange(string address, Action handler)
        {
            await _connection.InvokeAsync(SubscriptionMethod.SubscribeToOperations.ToString(), new
            {
                address,
                type = OperationType.Transaction.GetDescription()
            });

            _balanceChangedHandlers.AddOrUpdate(address, handler, (_, _) => handler);
        }
    }
}
