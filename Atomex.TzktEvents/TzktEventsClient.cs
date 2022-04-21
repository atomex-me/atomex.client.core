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

        public event EventHandler Connected;
        public event EventHandler Disconnected;

        private bool _isStarted;

        private readonly ILogger _log;
        private HubConnection _connection;
        private IAccountService _accountService;


        public TzktEventsClient(ILogger log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }


        public async Task StartAsync(string baseUri)
        {
            if (_isStarted)
            {
                _log.Warning($"Trying to start new connection with baseUri = {baseUri} while TzktEventsClient is already connected to {EventsUrl}.");
                return;
            }

            BaseUri = baseUri;

            _connection = new HubConnectionBuilder()
                .WithUrl(EventsUrl)
                .AddNewtonsoftJsonProtocol()
                .WithAutomaticReconnect(new RetryPolicy())
                .Build();
            
            _accountService = new AccountService(_connection, _log);

            _connection.Reconnecting += Reconnecting;
            _connection.Reconnected += Reconnected;
            _connection.Closed += Closed;

            SetSubscriptions();

            await _connection.StartAsync();
            _isStarted = true;

            await InitAsync();
            Connected?.Invoke(this, null);
        }

        public async Task StopAsync()
        {
            if (!_isStarted)
            {
                _log.Warning("Connection of TzktEventsClient was not started.");
                return;
            }

            _connection.Reconnecting -= Reconnecting;
            _connection.Reconnected -= Reconnected;
            _connection.Closed -= Closed;

            await _connection.StopAsync();
            await _connection.DisposeAsync();
            _isStarted = false;

            Disconnected?.Invoke(this, null);
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

        private Task Reconnecting(Exception exception = null)
        {
            if (exception != null)
            {
                _log.Warning($"Reconnecting to TzktEvents due to an error: {exception}.");
            }

            Disconnected?.Invoke(this, null);
            return Task.CompletedTask;
        }

        private async Task Reconnected(string connectionId)
        {
            _log.Debug($"Reconnected to TzKT Events with id: {connectionId}.");

            await InitAsync();
            Connected?.Invoke(this, null);
        }

        private async Task InitAsync()
        {
            await _accountService.InitAsync();
        }

        private async Task Closed(Exception exception = null)
        {
            if (exception != null)
            {
                _log.Warning($"Connection closed due to an error: {exception}. Reconnecting.");
            }

            await StopAsync();
        }

        private void SetSubscriptions()
        {
            _accountService.SetSubscriptions();
        }
    }
}
