using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomex.TzktEvents.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.TzktEvents.Services
{
    public class AccountService : IAccountService
    {
        private readonly HubConnection _connection;
        private readonly ILogger _log;

        private readonly ConcurrentDictionary<string, Action> _accountHandlers = new();
        private IDisposable _subscription;


        public AccountService(HubConnection connection, ILogger log)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task NotifyOnAccountAsync(string address, Action handler)
        {
            await _connection.InvokeAsync(SubscriptionMethod.SubscribeToAccounts.Method, new
            {
                addresses = new[] { address }
            });

            _accountHandlers.AddOrUpdate(address, handler, (_, _) => handler);
        }

        public async Task InitAsync()
        {
            if (!_accountHandlers.IsEmpty)
            {
                var addresses = _accountHandlers.Keys.ToArray();
                await _connection.InvokeAsync(SubscriptionMethod.SubscribeToAccounts.Method, new
                {
                    addresses
                });
            }
        }

        public void SetSubscriptions()
        {
            _subscription = _connection.On(SubscriptionMethod.SubscribeToAccounts.Channel, (JObject msg) =>
            {
                _log.Debug($"Has got msg from TzktEvents on 'operations' channel: {msg}.");

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
        }

        public void Dispose()
        {
            _subscription.Dispose();
        }
    }
}
