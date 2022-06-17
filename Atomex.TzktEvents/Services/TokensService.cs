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

        private readonly ConcurrentDictionary<string, TokenServiceSubscription> _addressSubs = new();
        private readonly Func<string, TokenServiceSubscription> _willNotBeCalled = _ => null;

        public TokensService(HubConnection hub, ILogger log)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task InitAsync()
        {
            if (_addressSubs.Skip(0).Count() != 0)
            {
                var addresses = _addressSubs.Select(a => a.Key);
                var subscriptionTasks = addresses.Select(address => _hub.InvokeAsync(
                    SubscriptionMethod.SubscribeToTokenBalances.Method, new
                    {
                        account = address
                    }));
            
                await Task.WhenAll(subscriptionTasks).ConfigureAwait(false);
            }
        }

        public void SetSubscriptions()
        {
            _hub.On<JObject>(SubscriptionMethod.SubscribeToTokenBalances.Channel, Handler);
        }

        public async Task NotifyOnTokenBalancesAsync(string address, Action<string, string, string> handler)
        {
            var subscription = new TokenServiceSubscription(handler);
            _addressSubs.AddOrUpdate(address, subscription, (_, _) => subscription);

            await _hub.InvokeAsync(SubscriptionMethod.SubscribeToTokenBalances.Method, new
            {
                account = address
            }).ConfigureAwait(false);
        }

        public async Task NotifyOnTokenBalancesAsync(IEnumerable<string> addresses, Action<string, string, string> handler)
        {
            var addressesList = addresses.ToList();
            if (addressesList.Count == 0)
            {
                _log.Warning("NotifyOnTokenBalancesAsync was called with empty list of addresses");
                return;
            }

            var subscription = new TokenServiceSubscription(handler);
            foreach (var address in addressesList)
            {
                _addressSubs.AddOrUpdate(address, subscription, (_, _) => subscription);
            }

            var subscriptionTasks = addressesList.Select(address => _hub.InvokeAsync(
                SubscriptionMethod.SubscribeToTokenBalances.Method, new
                {
                    account = address
                }));
            
            await Task.WhenAll(subscriptionTasks).ConfigureAwait(false);
        }

        private void Handler(JObject msg)
        {
            _log.Debug("Got msg from TzktEvents on '{Channel}' channel: {Message}",
                SubscriptionMethod.SubscribeToTokenBalances.Channel, msg.ToString());

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
                        SubscriptionMethod.SubscribeToTokenBalances.Channel, msg.ToString());
                    break;
            }
        }

        private void DataHandler(JToken data)
        {
            foreach (var @event in data)
            {
                var address = @event["account"]?["address"]?.ToString();
                var level = @event["lastLevel"]?.Value<int>() ?? 0;

                if (address == null || !_addressSubs.TryGetValue(address, out var subscription)) continue;

                if (level > subscription.LastState)
                {
                    var updatedSubscription = _addressSubs.AddOrUpdate(address, _willNotBeCalled, (_, existing) => existing with
                    {
                        LastState = level
                    });

                    var standard = @event["token"]?["standard"]?.ToString()?.Replace(".", "")?.ToUpper() ?? string.Empty;
                    var token = @event["token"]?["metadata"]?["symbol"]?.ToString()?.ToUpper() ?? string.Empty;

                    try
                    {
                        updatedSubscription.Handler(standard, token, address);
                    }
                    catch (Exception e)
                    {
                        _log.Error(e,"Error while calling subscriber handler on Data message with {{ standard: {Standard} ,token: {Token}, address: {Address} }}", standard, token, address);
                    }
                }
            }
        }

        private void ReorgHandler(JObject msg)
        {
            var state = msg["state"]?.Value<int>();
            if (state == null) return;

            foreach (var (address, subscription) in _addressSubs)
            {
                if (subscription.LastState != state)
                {
                    var updatedAccount = _addressSubs.AddOrUpdate(address, _willNotBeCalled, (_, existing) => existing with
                    {
                        LastState = state.Value
                    });

                    try
                    {
                        updatedAccount.Handler(string.Empty, string.Empty, address);
                    }
                    catch (Exception e)
                    {
                        _log.Error(e, "Error while calling subscriber handler on Reorg message for address: {Address}", address);
                    }
                }
            }
        }
    }
}
