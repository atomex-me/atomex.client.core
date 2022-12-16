using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Microsoft.Extensions.Logging;

using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.JsonRpc.Client.Streaming;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Reactive.Eth.Subscriptions;

namespace Atomex.Blockchain.Ethereum
{
    public class Erc20Notifier : IErc20Notifier
    {
        public string BaseUrl { get; }
        public string Currency { get; }
        public string ContractAddress { get; }

        private readonly ILogger _log;
        private readonly ConcurrentDictionary<string, Action<string, string>> _subscriptions = new();
        
        private bool _isRunning;
        private CancellationTokenSource _cts;
        private StreamingWebSocketClient _client;
        private readonly object[] _empty = {};

        private EthLogsObservableSubscription _subscriptionFrom;
        private EthLogsObservableSubscription _subscriptionTo;

        public Erc20Notifier(string baseUrl, string currency, string contractAddress, ILogger log)
        {
            BaseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task StartAsync()
        {
            if (_isRunning)
            {
                return;
            }

            try
            {
                _isRunning = true;
                _cts = new CancellationTokenSource();
                _client = new StreamingWebSocketClient(BaseUrl);
                
                await _client.StartAsync();

                _log.LogDebug("ERC20Notifier({Currency}) successfully started", Currency);
            }
            catch (OperationCanceledException)
            {
                _log.LogDebug("ERC20Notifier({Currency}).StartAsync canceled", Currency);
            }
            catch (Exception e)
            {
                _log.LogError(e, "Error on starting ERC20Notifier({Currency})", Currency);
            }
        }

        private void LogEventHandler(FilterLog log)
        {
            try
            {
                _log.LogInformation("ERC20Notifier({Currency}): Got log event for address {Address}", Currency, log.Address);

                var decoded = Event<TransferEventDTO>.DecodeEvent(log);

                if (decoded != null)
                {
                    InvokeHandler(decoded.Event.From.ToLower());
                    InvokeHandler(decoded.Event.To.ToLower());
                }
            }
            catch (Exception e)
            {
                _log.LogError(e, "ERC20Notifier: Log Address: " + log.Address + " is not a standard transfer log");
            }
        }

        private void InvokeHandler(string address)
        {
            if (!_subscriptions.TryGetValue(address, out var handler)) return;
            
            try
            {
                handler(Currency, address);
            }
            catch (Exception e)
            {
                _log.LogError(e, "ERC20Notifier: error on invoking event handler for address {Address}", address);
            }
        }

        private async Task InitiateSubscriptions()
        {
            if (_subscriptionFrom?.SubscriptionState == SubscriptionState.Subscribed)
            {
                await _subscriptionFrom.UnsubscribeAsync();
            }

            if (_subscriptionTo?.SubscriptionState == SubscriptionState.Subscribed)
            {
                await _subscriptionTo.UnsubscribeAsync();
            }

            _subscriptionFrom = new EthLogsObservableSubscription(_client);
            _subscriptionTo = new EthLogsObservableSubscription(_client);
                
            _subscriptionFrom.GetSubscriptionDataResponsesAsObservable().Subscribe(LogEventHandler);
            _subscriptionTo.GetSubscriptionDataResponsesAsObservable().Subscribe(LogEventHandler);

            if (!_subscriptions.Skip(0).Any())
            {
                return;
            }
            var addresses = _subscriptions.Select(s => s.Key).ToArray();
            // create a log filter specific to Transfers
            // this filter will match any Transfer (matching the signature) 
            var filterTransfersFrom = Event<TransferEventDTO>
                .GetEventABI()
                .CreateFilterInput(ContractAddress, _empty, addresses, _empty);

            var filterTransfersTo = Event<TransferEventDTO>
                .GetEventABI()
                .CreateFilterInput(ContractAddress, _empty, _empty, addresses);

            await _subscriptionFrom.SubscribeAsync(filterTransfersFrom);
            await _subscriptionTo.SubscribeAsync(filterTransfersTo);
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
            {
                return;
            }

            try
            {
                _cts.Cancel();

                await _subscriptionFrom.UnsubscribeAsync();
                await _subscriptionTo.UnsubscribeAsync();
                await _client.StopAsync();

                _client.Dispose();
                _subscriptions.Clear();

                _log.LogDebug("ERC20Notifier({Currency}) successfully stopped", Currency);
            }
            catch (Exception e)
            {
                _log.LogError(e, "Error on stopping ERC20Notifier({Currency})", Currency);
            }
            finally
            {
                _isRunning = false;
            }
        }

        public Task SubscribeOnEventsAsync(string address, Action<string, string> handler)
        {
            _subscriptions.AddOrUpdate(address.ToUpper(), 
                (_) => handler, 
                (_, _) => handler
            );

            return InitiateSubscriptions();
        }

        public Task SubscribeOnEventsAsync(IEnumerable<string> addresses, Action<string, string> handler)
        {
            foreach (var address in addresses) 
            {
                _subscriptions.AddOrUpdate(address.ToLower(),
                    handler, 
                    (_, _) => handler
                );
            }

            return InitiateSubscriptions();
        }
    }

    [Event("Transfer")]
    public class TransferEventDTO : IEventDTO
    {
        [Parameter("address", "_from", 1, true)]
        public virtual string From { get; set; }

        [Parameter("address", "_to", 2, true)]
        public virtual string To { get; set; }

        [Parameter("uint256", "_value", 3, false)]
        public virtual BigInteger Value { get; set; }
    }
}