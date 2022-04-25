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
        public string EventsUrl => $"{_baseUri}/events";

        public event EventHandler Connected;
        public event EventHandler Reconnecting;
        public event EventHandler Disconnected;

        private string _baseUri;
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
                _log.Warning("Trying to start new connection with baseUri = {BaseUri} while TzktEventsClient is already connected to {EventsUrl}",
                    _baseUri, EventsUrl);
                return;
            }

            _baseUri = baseUri;

            try
            {
                _connection = new HubConnectionBuilder()
                .WithUrl(EventsUrl)
                .AddNewtonsoftJsonProtocol()
                .WithAutomaticReconnect(new RetryPolicy())
                .Build();

                _accountService = new AccountService(_connection, _log);

                _connection.Reconnecting += ReconnectingHandler;
                _connection.Reconnected += ReconnectedHandler;
                _connection.Closed += ClosedHandler;

                SetSubscriptions();

                await _connection.StartAsync().ConfigureAwait(false);
                _isStarted = true;

                await InitAsync().ConfigureAwait(false);
                
                Connected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _log.Error(ex, ex.Message);
                _isStarted = false;
            }
        }

        public async Task StopAsync()
        {
            if (!_isStarted)
            {
                _log.Warning("Connection of TzktEventsClient was not started");
                return;
            }
            
            _connection.Reconnecting -= ReconnectingHandler;
            _connection.Reconnected -= ReconnectedHandler;
            _connection.Closed -= ClosedHandler;

            try
            {
                await _connection.StopAsync().ConfigureAwait(false);
                await _connection.DisposeAsync().ConfigureAwait(false);
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _log.Error(ex, ex.Message);
            }
            finally
            {
                _isStarted = false;
            }
        }

        public async Task NotifyOnAccountAsync(string address, Action handler)
        {
            if (!_isStarted)
            {
                _log.Error("NotifyOnAccountAsync was called before established connection to Tzkt Events");
                return;
            }

            await _accountService.NotifyOnAccountAsync(address, handler).ConfigureAwait(false);
        }

        private Task ReconnectingHandler(Exception exception = null)
        {
            if (exception != null)
            {
                _log.Warning("ReconnectingHandler to TzktEvents due to an error: {Exception}", exception);
            }

            try
            {
                Reconnecting?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _log.Error(ex, ex.Message);
            }

            return Task.CompletedTask;
        }

        private async Task ReconnectedHandler(string connectionId)
        {
            _log.Debug("ReconnectedHandler to TzKT Events with id: {ConnectionId}", connectionId);
            await InitAsync().ConfigureAwait(false);

            try
            {
                Connected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _log.Error(ex, ex.Message);
            }
        }

        private async Task ClosedHandler(Exception exception = null)
        {
            if (exception != null)
            {
                _log.Warning("Connection closed due to an error: {Exception}", exception);
            }

            await StopAsync().ConfigureAwait(false);
        }

        private async Task InitAsync()
        {
            try
            {
                await _accountService.InitAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error(ex, ex.Message);
            }
        }

        private void SetSubscriptions()
        {
            try
            {
                _accountService.SetSubscriptions();
            }
            catch (Exception ex)
            {
                _log.Error(ex, ex.Message);
            }
        }
    }
}
