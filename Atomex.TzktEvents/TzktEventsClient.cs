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
        private string EventsUrl => $"https://{BaseUri}/v1/events";

        private HubConnection _connection;
        private readonly IHubConnectionCreator _hubConnectionCreator;
        private bool _isStarted;


        public TzktEventsClient(IHubConnectionCreator hubConnectionCreator)
        {
            _hubConnectionCreator = hubConnectionCreator ?? throw new ArgumentNullException(nameof(hubConnectionCreator));
        }

        private async Task Init(Exception? arg = null)
        {
            await _connection.StartAsync();
            // TODO: Add subscribers on channels.
        }

        public async Task Start(string baseUri)
        {
            if (_isStarted)
            {
                Log.Warning($"Trying to start new connection with baseUri = {baseUri} while TzktEventsClient is already connected to {EventsUrl}.");
                return;
            }

            _isStarted = true;
            BaseUri = baseUri;

            _connection = _hubConnectionCreator.Create(EventsUrl);
            _connection.Closed += Init;

            await Init();
        }

        public async Task Stop()
        {
            if (!_isStarted)
            {
                Log.Warning("Connection of TzktEventsClient was already stopped.");
                return;
            }

            _connection.Closed -= Init;
            await _connection.StopAsync();
            await _connection.DisposeAsync();
            _isStarted = false;
        }
    }
}
