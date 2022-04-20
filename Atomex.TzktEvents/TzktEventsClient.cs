using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
using Atomex.TzktEvents.Services;
using Microsoft.Extensions.DependencyInjection;


namespace Atomex.TzktEvents
{
    public class TzktEventsClient : ITzktEventsClient
    {
        public string BaseUri { get; private set; }
        public string EventsUrl => $"{BaseUri}/events";

        private bool _isStarted;

        private readonly ILogger _log;
        private HubConnection _connection;
        private IAccountService _accountService;


        public TzktEventsClient(ILogger log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        private async Task InitAsync(Exception exception = null)
        {
            if (exception != null)
            {
                _log.Warning($"Connection closed due to an error: {exception}. Reconnecting.");
            }

            await _connection.StartAsync();
            _log.Debug($"Established connection to TzKT Events with id: {_connection.ConnectionId}.");


            await _accountService.InitAsync();
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

            _connection = new HubConnectionBuilder()
                .WithUrl(EventsUrl)
                .AddNewtonsoftJsonProtocol()
                .Build();
            
            _accountService = new AccountService(_connection, _log);

            _connection.Closed += InitAsync;
            SetSubscriptions();

            await InitAsync();
        }

        private void SetSubscriptions()
        {
            _accountService.SetSubscriptions();
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

        public async Task NotifyOnAccountAsync(string address, Action handler)
        {
            if (!_isStarted)
            {
                // Throw?
                _log.Error("NotifyOnAccountAsync was called before established connection to Tzkt Events.");
                return;
            }

            await _accountService.NotifyOnAccountAsync(address, handler);
        }
    }
}
