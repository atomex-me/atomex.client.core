﻿#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Security;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Atomex.TzktEvents.Models;
using Atomex.TzktEvents.Services;

namespace Atomex.TzktEvents
{
    public class TzktEventsClient : ITzktEventsClient
    {
        public static Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>? ServerCertificateCustomValidationCallback;
        public string EventsUrl => $"{_baseUri}events";

        public event EventHandler? Connected;
        public event EventHandler? Reconnecting;
        public event EventHandler? Disconnected;

        private string? _baseUri;
        private bool _isStarted;

        private readonly ILogger? _log;
        private HubConnection? _hub;
        private IAccountService? _accountService;
        private ITokensService? _tokensService;

        public TzktEventsClient(ILogger? log = null)
        {
            _log = log;
        }

        public async Task StartAsync(string baseUri)
        {
            if (_isStarted)
            {
                if (baseUri == _baseUri)
                    return;

                _log?.LogWarning("Trying to start new connection with baseUri = {@baseUri} while TzktEventsClient is already connected to {@eventsUrl}",
                    _baseUri, EventsUrl);

                return;
            }

            _baseUri = baseUri;

            try
            {
                _hub = new HubConnectionBuilder()
                    .WithUrl(EventsUrl, o =>
                    {
                        if (ServerCertificateCustomValidationCallback != null)
                        {
                            o.HttpMessageHandlerFactory = h =>
                            {
                                if (h is HttpClientHandler httpClientHandler)
                                {
                                    httpClientHandler.ServerCertificateCustomValidationCallback =
                                        ServerCertificateCustomValidationCallback;
                                }

                                return h;
                            };
                        }
                    })
                    .AddNewtonsoftJsonProtocol()
                    .WithAutomaticReconnect(new RetryPolicy())
                    .Build();

                _accountService = new AccountService(_hub, _log);
                _tokensService = new TokensService(_hub, _log);

                _hub.Reconnecting += ReconnectingHandler;
                _hub.Reconnected += ReconnectedHandler;
                _hub.Closed += ClosedHandler;

                SetSubscriptions();

                await _hub
                    .StartAsync()
                    .ConfigureAwait(false);

                _isStarted = true;

                await InitAsync()
                    .ConfigureAwait(false);

                Connected?.Invoke(this, EventArgs.Empty);

                _log?.LogInformation("TzktEventsClient started with events url: {@eventsUrl}", EventsUrl);
            }
            catch (Exception e)
            {
                _log?.LogError(e, "TzktEventsClient failed to start");
                _isStarted = false;
            }
        }

        public async Task StopAsync()
        {
            if (!_isStarted || _hub == null)
            {
                _log?.LogWarning("Connection of TzktEventsClient was not started");
                return;
            }

            _hub.Reconnecting -= ReconnectingHandler;
            _hub.Reconnected -= ReconnectedHandler;
            _hub.Closed -= ClosedHandler;

            try
            {
                await _hub
                    .StopAsync()
                    .ConfigureAwait(false);

                await _hub
                    .DisposeAsync()
                    .ConfigureAwait(false);

                Disconnected?.Invoke(this, EventArgs.Empty);

                _log?.LogInformation("TzktEventsClient stopped");
            }
            catch (Exception e)
            {
                _log?.LogError(e, "TzktEventsClient was stopped with error");
            }
            finally
            {
                _isStarted = false;
            }
        }

        public async Task NotifyOnAccountAsync(string address, Action<string> handler)
        {
            if (CheckIsStarted(nameof(NotifyOnAccountAsync)) && _accountService != null)
            {
                await _accountService
                    .NotifyOnAccountAsync(address, handler)
                    .ConfigureAwait(false);
            }
        }

        public async Task NotifyOnAccountsAsync(IEnumerable<string> addresses, Action<string> handler)
        {
            if (CheckIsStarted(nameof(NotifyOnAccountsAsync)) && _accountService != null)
            {
                await _accountService
                    .NotifyOnAccountsAsync(addresses, handler)
                    .ConfigureAwait(false);
            }
        }

        public async Task NotifyOnTokenBalancesAsync(string address, Action<TezosTokenEvent> handler)
        {
            if (CheckIsStarted(nameof(NotifyOnTokenBalancesAsync)) && _tokensService != null)
            {
                await _tokensService
                    .NotifyOnTokenBalancesAsync(address, handler)
                    .ConfigureAwait(false);
            }
        }

        public async Task NotifyOnTokenBalancesAsync(IEnumerable<string> addresses, Action<TezosTokenEvent> handler)
        {
            if (CheckIsStarted(nameof(NotifyOnTokenBalancesAsync)) && _tokensService != null)
            {
                await _tokensService
                    .NotifyOnTokenBalancesAsync(addresses, handler)
                    .ConfigureAwait(false);
            }
        }

        private bool CheckIsStarted(string methodName)
        {
            if (!_isStarted)
            {
                _log?.LogError("{@methodName} was called before established connection to Tzkt Events", methodName);
                return false;
            }

            return true;
        }

        private Task ReconnectingHandler(Exception? exception = null)
        {
            if (exception != null)
            {
                _log?.LogWarning("Reconnecting to TzktEvents due to an error: {@exception}", exception);
            }

            try
            {
                Reconnecting?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, ex.Message);
            }

            return Task.CompletedTask;
        }

        private async Task ReconnectedHandler(string? connectionId)
        {
            _log?.LogDebug("Reconnected to TzKT Events with connection id: {@connectionId}", connectionId);

            await InitAsync()
                .ConfigureAwait(false);

            try
            {
                Connected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, ex.Message);
            }
        }

        private async Task ClosedHandler(Exception? exception = null)
        {
            if (exception != null)
            {
                _log?.LogWarning("Connection closed due to an error: {@exception}", exception);
            }

            await StopAsync()
                .ConfigureAwait(false);
        }

        private async Task InitAsync()
        {
            try
            {
                var initTasks = new []
                {
                    _accountService?.InitAsync(),
                    _tokensService?.InitAsync(),
                };

                await Task
                    .WhenAll(initTasks)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log?.LogError(e, "TzktEvents caught error on initialization");
            }
        }

        private void SetSubscriptions()
        {
            try
            {
                _accountService?.SetSubscriptions();
                _tokensService?.SetSubscriptions();
            }
            catch (Exception e)
            {
                _log?.LogError(e, "TzktEvents caught error while setting subscription");
            }
        }
    }
}