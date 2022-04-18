using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;


namespace Atomex.TzktEvents
{
    public class TzktEventsClient : ITzktEventsClient
    {
        public string BaseUri { get; }
        private string EventsUri => $"https://{BaseUri}/v1/events";

        private readonly HubConnection _connection;


        public TzktEventsClient(string baseUri)
        {
            BaseUri = baseUri;

            _connection = new HubConnectionBuilder()
                .WithUrl(EventsUri)
                .Build();

            _connection.Closed += Init;
        }

        private async Task Init(Exception? arg = null)
        {
            await _connection.StartAsync();
            // TODO: Add subscribers on channels.
        }

        public async Task Start()
        {
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
