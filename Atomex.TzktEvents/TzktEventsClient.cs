using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;

using Atomex.TzktEvents.Services;


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
            // TODO: Add subscribers on channels.
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
                _log.Warning("Connection of TzktEventsClient was already stopped.");
                return;
            }

            _connection.Closed -= InitAsync;
            await _connection.StopAsync();
            await _connection.DisposeAsync();
            _isStarted = false;
        }
    }
}
