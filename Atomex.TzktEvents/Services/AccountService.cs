using System;
using System.Collections.Concurrent;
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

        private readonly ConcurrentDictionary<string, AccountSubscription> _accounts = new();
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

            var account = new AccountSubscription(address, handler);
            _accounts.AddOrUpdate(address, account, (_, _) => account);
        }

        public async Task InitAsync()
        {
            if (!_accounts.IsEmpty)
            {
                var addresses = _accounts.Keys.ToArray();
                await _connection.InvokeAsync(SubscriptionMethod.SubscribeToAccounts.Method, new
                {
                    addresses
                });
            }
        }

        public void SetSubscriptions()
        {
            _subscription = _connection.On<JObject>(SubscriptionMethod.SubscribeToAccounts.Channel, Handler);
        }

        private void Handler(JObject msg)
        {
            _log.Debug($"Got msg from TzktEvents on '{SubscriptionMethod.SubscribeToAccounts.Channel}' channel: {msg}.");

            var messageType = (MessageType?)msg["type"]?.Value<int>();
            switch (messageType)
            {
                case MessageType.State:
                    break;

                case MessageType.Data:
                    foreach (var accountEvent in msg["data"])
                    {
                        var address = accountEvent["address"]?.ToString();
                        if (address == null || !_accounts.TryGetValue(address, out var account)) continue;
                        
                        lock (account)
                        {
                            var lastActivity = accountEvent["lastActivity"]?.Value<int>() ?? 0;
                            if (lastActivity > account.LastState)
                            {
                                account.LastState = lastActivity;
                                account.Handler();
                            }
                        }
                    }

                    break;

                case MessageType.Reorg:
                    var state = msg["state"]?.Value<int>();
                    if (state == null) break;

                    foreach (var account in _accounts.Values)
                    {
                        lock (account)
                        {
                            if (account.LastState != state)
                            {
                                account.LastState = state.Value;
                                account.Handler();
                            }
                        }
                    }

                    break;

                default:
                    _log.Warning($"Got msg with unrecognizable type from TzktEvents on '{SubscriptionMethod.SubscribeToAccounts.Channel}' channel: {msg}.");
                    break;
            }
        }
        

        public void Dispose()
        {
            _subscription.Dispose();
        }
    }
}
