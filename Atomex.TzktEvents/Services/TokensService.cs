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
    public class TokensService : ITokensService
    {
        private readonly HubConnection _hub;
        private readonly ILogger _log;

        private readonly ConcurrentDictionary<string, AccountSubscription> _accounts = new();
        private readonly Func<string, AccountSubscription> _willNotBeCalled = _ => null;

        public TokensService(HubConnection hub, ILogger log)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task InitAsync()
        {
            if (_accounts.Skip(0).Count() != 0)
            {
                var addresses = _accounts.Select(a => a.Key).ToArray();
                await _hub.InvokeAsync(SubscriptionMethod.SubscribeToTokenBalances.Method, new
                {
                    addresses
                }).ConfigureAwait(false);
            }
        }

        public void SetSubscriptions()
        {
            _hub.On<JObject>(SubscriptionMethod.SubscribeToTokenBalances.Channel, Handler);
        }

        public async Task NotifyOnTokenBalancesAsync(string address, Action<string> handler)
        {
            var account = new AccountSubscription(handler);
            _accounts.AddOrUpdate(address, account, (_, _) => account);

            await _hub.InvokeAsync(SubscriptionMethod.SubscribeToTokenBalances.Method, new
            {
                addresses = new[] { address }
            }).ConfigureAwait(false);
        }

        public async Task NotifyOnTokenBalancesAsync(IEnumerable<string> addresses, Action<string> handler)
        {
            var addressesList = addresses.ToList();
            if (addressesList.Count == 0)
            {
                _log.Warning("NotifyOnTokenBalancesAsync was called with empty list of addresses");
                return;
            }

            var account = new AccountSubscription(handler);
            foreach (var address in addressesList)
            {
                _accounts.AddOrUpdate(address, account, (_, _) => account);
            }

            await _hub.InvokeAsync(SubscriptionMethod.SubscribeToTokenBalances.Method, new
            {
                addresses = addressesList
            }).ConfigureAwait(false);
        }

        private void Handler(JObject obj)
        {
            throw new NotImplementedException();
        }
    }
}
