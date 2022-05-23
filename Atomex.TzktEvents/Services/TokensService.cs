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

        public Task NotifyOnTokenBalancesAsync(string address, Action<string> handler)
        {
            throw new NotImplementedException();
        }

        public Task NotifyOnTokenBalancesAsync(IEnumerable<string> addresses, Action<string> handler)
        {
            throw new NotImplementedException();
        }

        private void Handler(JObject obj)
        {
            throw new NotImplementedException();
        }
    }
}
