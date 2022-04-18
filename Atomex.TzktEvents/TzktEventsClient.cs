using System;
using System.Threading.Tasks;
using Atomex.TzktEvents.Services;
using Microsoft.AspNetCore.SignalR.Client;


namespace Atomex.TzktEvents
{
    public class TzktEventsClient : ITzktEventsClient
    {
        public string BaseUri { get; private set; }
        private string EventsUrl => $"https://{BaseUri}/v1/events";

        private HubConnection _connection;
        private readonly IHubConnectionCreator _hubConnectionCreator;


        public TzktEventsClient(IHubConnectionCreator hubConnectionCreator)
        {
            _hubConnectionCreator = hubConnectionCreator;
        }

        private async Task Init(Exception? arg = null)
        {
            await _connection.StartAsync();
            // TODO: Add subscribers on channels.
        }

        public async Task Start(string baseUri)
        {
            BaseUri = baseUri;

            _connection = _hubConnectionCreator.Create(EventsUrl);
            _connection.Closed += Init;

            await Init();
        }

        public async Task Stop()
        {
            _connection.Closed -= Init;
            await _connection.StopAsync();
            await _connection.DisposeAsync();
        }
    }
}
