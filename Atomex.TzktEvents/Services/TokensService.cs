﻿#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

using Atomex.TzktEvents.Models;

namespace Atomex.TzktEvents.Services
{
    public class TokensService : ITokensService
    {
        private readonly HubConnection _hub;
        private readonly ILogger? _log;

        private readonly ConcurrentDictionary<string, TokenServiceSubscription> _addressSubscriptions = new();
        private readonly Func<string, TokenServiceSubscription> _willNotBeCalled = _ => null;

        public TokensService(HubConnection hub, ILogger? log = null)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _log = log;
        }

        public async Task InitAsync()
        {
            if (_addressSubscriptions.Skip(0).Count() != 0)
            {
                var addresses = _addressSubscriptions.Select(a => a.Key);

                var subscriptionTasks = addresses.Select(address => _hub.InvokeAsync(
                    SubscriptionMethod.SubscribeToTokenBalances.Method, new
                    {
                        account = address
                    }));
            
                await Task
                    .WhenAll(subscriptionTasks)
                    .ConfigureAwait(false);
            }
        }

        public void SetSubscriptions()
        {
            _hub.On<JObject>(SubscriptionMethod.SubscribeToTokenBalances.Channel, Handler);
        }

        public async Task NotifyOnTokenBalancesAsync(string address, Action<TezosTokenEvent> handler)
        {
            var subscription = new TokenServiceSubscription(handler);

            _addressSubscriptions.AddOrUpdate(address, subscription, (_, _) => subscription);

            await _hub
                .InvokeAsync(SubscriptionMethod.SubscribeToTokenBalances.Method, new
                {
                    account = address
                })
                .ConfigureAwait(false);
        }

        public async Task NotifyOnTokenBalancesAsync(IEnumerable<string> addresses, Action<TezosTokenEvent> handler)
        {
            var addressesList = addresses.ToList();

            if (addressesList.Count == 0)
            {
                _log?.LogWarning("NotifyOnTokenBalancesAsync was called with empty list of addresses");
                return;
            }

            var subscription = new TokenServiceSubscription(handler);

            foreach (var address in addressesList)
            {
                _addressSubscriptions.AddOrUpdate(address, subscription, (_, _) => subscription);
            }

            var subscriptionTasks = addressesList.Select(address => _hub.InvokeAsync(
                SubscriptionMethod.SubscribeToTokenBalances.Method, new
                {
                    account = address
                }));
            
            await Task
                .WhenAll(subscriptionTasks)
                .ConfigureAwait(false);
        }

        private void Handler(JObject msg)
        {
            _log?.LogDebug("Got msg from TzktEvents on '{@channel}' channel: {@message}",
                SubscriptionMethod.SubscribeToTokenBalances.Channel,
                msg.ToString());

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
                    _log?.LogWarning("Got msg with unrecognizable type from TzktEvents on '{@channel}' channel: {@message}",
                        SubscriptionMethod.SubscribeToTokenBalances.Channel,
                        msg.ToString());
                    break;
            }
        }

        private void DataHandler(JToken data)
        {
            var subscriptionsStateUpdates = new Dictionary<string, long>();

            foreach (var @event in data)
            {
                var address = @event["account"]?["address"]?.ToString();
                var level = @event["lastLevel"]?.Value<int>() ?? 0;

                if (address == null || !_addressSubscriptions.TryGetValue(address, out var subscription))
                    continue;

                if (level > subscription.LastState)
                {
                    subscriptionsStateUpdates[address] = level;
                    
                    try
                    {
                        var standard = @event["token"]?["standard"]?.ToString()?.Replace(".", "")?.ToUpper() ?? string.Empty;
                        var contract = @event["token"]?["contract"]?["address"]?.ToString() ?? string.Empty;

                        decimal.TryParse(@event["token"]?["tokenId"]?.ToString(), out var tokenId);

                        var token = @event["token"]?["metadata"]?["symbol"]?.ToString()?.ToUpper() ?? string.Empty;
                        var tezosTokenEvent = new TezosTokenEvent(standard, contract, tokenId, token, address);

                        subscription.Handler(tezosTokenEvent);
                    }
                    catch (Exception e)
                    {
                        _log?.LogError(e,"Error while calling subscriber handler on Data message for address {@address}", address);
                    }
                }
            }

            foreach (var (address, level) in subscriptionsStateUpdates)
            {
                _addressSubscriptions.AddOrUpdate(address, _willNotBeCalled, (_, existing) => existing with
                {
                    LastState = level
                });
            }
        }

        private void ReorgHandler(JObject msg)
        {
            var state = msg["state"]?.Value<int>();

            if (state == null)
                return;

            foreach (var (address, subscription) in _addressSubscriptions)
            {
                if (state < subscription.LastState)
                {
                    var updatedAccount = _addressSubscriptions.AddOrUpdate(address, _willNotBeCalled, (_, existing) => existing with
                    {
                        LastState = state.Value
                    });

                    try
                    {
                        var tezosTokenEvent = new TezosTokenEvent(address);
                        updatedAccount.Handler(tezosTokenEvent);
                    }
                    catch (Exception e)
                    {
                        _log?.LogError(e, "Error while calling subscriber handler on Reorg message for address: {@address}", address);
                    }
                }
            }
        }
    }
}