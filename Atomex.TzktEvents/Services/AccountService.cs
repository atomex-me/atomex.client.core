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
        private readonly HubConnection _hub;
        private readonly ILogger _log;

        private readonly ConcurrentDictionary<string, AccountSubscription> _accounts = new();
        private readonly Func<string, AccountSubscription> _willNotBeCalled = _ => null;

        public AccountService(HubConnection hub, ILogger log)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task NotifyOnAccountAsync(string address, Action<string> handler)
        {
            var account = new AccountSubscription(handler);
            _accounts.AddOrUpdate(address, account, (_, _) => account);

            await _hub.InvokeAsync(SubscriptionMethod.SubscribeToAccounts.Method, new
            {
                addresses = new[] { address }
            }).ConfigureAwait(false);
        }

        public async Task NotifyOnAccountsAsync(IEnumerable<string> addresses, Action<string> handler)
        {
            var addressesList = addresses.ToList();
            if (addressesList.Count == 0)
            {
                _log.Error("NotifyOnAccountsAsync was called with empty list of addresses");
                return;
            }

            foreach (var address in addressesList)
            {
                var account = new AccountSubscription(handler);
                _accounts.AddOrUpdate(address, account, (_, _) => account);
            }

            await _hub.InvokeAsync(SubscriptionMethod.SubscribeToAccounts.Method, new
            {
                addresses = addressesList
            }).ConfigureAwait(false);
        }

        private void Handler(JObject msg)
        {
            _log.Debug("Got msg from TzktEvents on '{Channel}' channel: {Message}",
                SubscriptionMethod.SubscribeToAccounts.Channel, msg.ToString());

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
                        SubscriptionMethod.SubscribeToAccounts.Channel, msg.ToString());
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
                        updatedAccount.Handler(address);
                    }
                    catch (Exception e)
                    {
                        _log.Error(e,"Error while calling subscriber handler on Data message");
                    }
                }
            }
        }

        private void ReorgHandler(JObject msg)
        {
            var state = msg["state"]?.Value<int>();
            if (state == null) return;

            foreach (var pair in _accounts)
            {
                var account = pair.Value;
                var address = pair.Key;

                if (account.LastState != state)
                {
                    var updatedAccount = _accounts.AddOrUpdate(address, _willNotBeCalled, (_, existing) => existing with
                    {
                        LastState = state.Value
                    });

                    try
                    {
                        updatedAccount.Handler(address);
                    }
                    catch (Exception e)
                    {
                        _log.Error(e, "Error while calling subscriber handler on Reorg message");
                    }
                }
            }
        }
    }
}
