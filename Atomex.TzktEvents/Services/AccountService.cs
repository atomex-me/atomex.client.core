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
    internal record AccountSubscription(Action Handler, int LastState = 0);

    public class AccountService : IAccountService
    {
        private readonly HubConnection _connection;
        private readonly ILogger _log;

        private readonly ConcurrentDictionary<string, AccountSubscription> _accounts = new();
        private IDisposable _subscription;

        private readonly Func<string, AccountSubscription> _willNotBeCalled = _ => null;

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
            }).ConfigureAwait(false);

            var account = new AccountSubscription(handler);
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
                }).ConfigureAwait(false);
            }
        }

        public void SetSubscriptions()
        {
            _subscription = _connection.On<JObject>(SubscriptionMethod.SubscribeToAccounts.Channel, Handler);
        }

        private void Handler(JObject msg)
        {
            _log.Debug("Got msg from TzktEvents on '{Channel}' channel: {Message}",
                SubscriptionMethod.SubscribeToAccounts.Channel, msg);

            var messageType = (MessageType?)msg["type"]?.Value<int>();
            switch (messageType)
            {
                case MessageType.State:
                    break;

                case MessageType.Data:
                    DataHandler(msg["data"]);
                    break;

                case MessageType.Reorg:
                    ReorgHandler(msg);
                    break;

                default:
                    _log.Warning("Got msg with unrecognizable type from TzktEvents on '{Channel}' channel: {Message}",
                        SubscriptionMethod.SubscribeToAccounts.Channel, msg);
                    break;
            }
        }

        private void DataHandler(JToken data)
        {
            foreach (var accountEvent in data)
            {
                var address = accountEvent["address"]?.ToString();
                if (address == null || !_accounts.TryGetValue(address, out var account)) continue;

                var lastActivity = accountEvent["lastActivity"]?.Value<int>() ?? 0;

                if (lastActivity > account.LastState)
                {
                    var updatedAccount = _accounts.AddOrUpdate(address, _willNotBeCalled, (_, existing) => existing with
                    {
                        LastState = lastActivity
                    });

                    try
                    {
                        updatedAccount.Handler();
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, ex.Message);
                    }
                }
            }
        }

        private void ReorgHandler(JObject msg)
        {
            var state = msg["state"]?.Value<int>();
            if (state == null) return;

            foreach (var (address, account) in _accounts)
            {
                if (account.LastState != state)
                {
                    var updatedAccount = _accounts.AddOrUpdate(address, _willNotBeCalled, (_, existing) => existing with
                    {
                        LastState = state.Value
                    });

                    try
                    {
                        updatedAccount.Handler();
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, ex.Message);
                    }
                }
            }
        }
        

        public void Dispose() => _subscription?.Dispose();
    }
}
