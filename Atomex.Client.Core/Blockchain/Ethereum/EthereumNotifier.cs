﻿using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Atomex.Common;
using Atomex.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;


namespace Atomex.Blockchain.Ethereum
{
    internal record Subscription(Action<string> Handler, int StartBlock = 6980640); // TODO: Get current last block as initial value

    public class EthereumNotifier : IEthereumNotifier
    {
        public string BaseUrl { get; }
        private readonly string _eventsWs;
        private readonly ILogger _log;

        private WebSocketClient _events;
        private bool _isRunning;

        private readonly ConcurrentDictionary<string, Subscription> _subscriptions = new();

        private readonly TimeSpan _transactionsDelay = TimeSpan.FromSeconds(15);
        private const int MinDelayBetweenRequestMs = 6000;
        private static readonly RequestLimitControl RequestLimitControl 
            = new(MinDelayBetweenRequestMs);
        private CancellationTokenSource _cts;

        public EthereumNotifier(string baseUrl, string eventsWs, ILogger log)
        {
            BaseUrl = baseUrl;
            _eventsWs = eventsWs;
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

                _events = new WebSocketClient(_eventsWs);
                await _events.ConnectAsync();

                RunBalanceChecker();
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on starting EthereumNotifier");
            }
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

                await _events.CloseAsync();
                _subscriptions.Clear();
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on stopping EthereumNotifier");

            }
            finally
            {
                _isRunning = false;
            }
        }

        public Task SubscribeOnBalanceUpdate(string address, Action<string> handler)
        {
            _subscriptions.AddOrUpdate(address, 
                (_) => new Subscription(handler), 
                (_, sub) => sub with { Handler = handler }
            );
            return Task.CompletedTask;
        }

        public Task SubscribeOnBalanceUpdate(IEnumerable<string> addresses, Action<string> handler)
        {
            var subscription = new Subscription(handler);

            foreach (var address in addresses)
            {
                _subscriptions.AddOrUpdate(address,
                    subscription, 
                    (_, sub) => sub with { Handler = handler }
                );
            }

            return Task.CompletedTask;
        }

        private void RunBalanceChecker()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    foreach (var (address, subscription) in _subscriptions)
                    {
                        await RequestLimitControl
                            .Wait(_cts.Token)
                            .ConfigureAwait(false);
                        
                        var requestUri = $"api?module=account" +
                                         "&action=txlist" +
                                         $"&address={address}" +
                                         "&tag=latest" +
                                         "&page=1" +
                                         $"&startBlock={subscription.StartBlock}";
                        
                        var resultLength = await HttpHelper.GetAsyncResult<int>(
                                baseUri: BaseUrl,
                                requestUri: requestUri,
                                responseHandler: (_, content) =>
                                {
                                    _log.Information("Got from etherscan.io: {@Content}", content);
                                    var json = JsonConvert.DeserializeObject<JObject>(content);

                                    if (json.ContainsKey("status") && json["status"]!.ToString() != "1")
                                    {
                                        _log.Warning("Status is NOTOK from Etherscan, response: {@Response}", json.ToString());
                                    }

                                    if (json.ContainsKey("result"))
                                    {
                                        var length = json["result"]!.Count();
                                        var blockNumber = length > 0
                                            ? json["result"]![length - 1]!["blockNumber"]!.Value<int>()
                                            : subscription.StartBlock;

                                        _log.Information("Length: {length}, blockNumber: {blockNumber}, current startBlock: {startBlock}", length, blockNumber, subscription.StartBlock);
                                        var updateResult = _subscriptions.TryUpdate(address,
                                            subscription with {StartBlock = blockNumber + 1},
                                            subscription
                                        );

                                        if (!updateResult)
                                        {
                                            _log.Warning("Could not update start block of subscription for address {Address}", address);
                                        }

                                        return length;
                                    }
                                    
                                    return 0;
                                },
                                cancellationToken: _cts.Token)
                            .ConfigureAwait(false);

                        _log.Information("Got resultLength = {ResultLength}", resultLength.Value);
                        if (resultLength.Value > 0)
                        {
                            try
                            {
                                subscription.Handler(address);
                            }
                            catch (Exception e)
                            {
                                _log.Error(e, "Caught error on ether balance updated handler call");
                            }
                        }
                    }

                    await Task.Delay(_transactionsDelay);
                }
            }, _cts.Token);
        }
    }
}
