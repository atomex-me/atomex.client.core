using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Atomex.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;


namespace Atomex.Blockchain.Ethereum
{
    internal record Subscription(Action<string> Handler, int StartBlock = 6980640); // TODO: Get current last block as initial value

    public class EthereumNotifier : IEthereumNotifier
    {
        public string BaseUrl { get; }
        private readonly ILogger _log;
        private readonly ConcurrentDictionary<string, Subscription> _subscriptions = new();

        private bool _isRunning;
        private CancellationTokenSource _cts;

        private readonly TimeSpan _transactionsDelay = TimeSpan.FromSeconds(15);
        private const int MinDelayBetweenRequestMs = 6000;
        private static readonly RequestLimitControl RequestLimitControl 
            = new(MinDelayBetweenRequestMs);

        public EthereumNotifier(string baseUrl, ILogger log)
        {
            BaseUrl = baseUrl;
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            try
            {
                _isRunning = true;
                _cts = new CancellationTokenSource();

                RunBalanceChecker();
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on starting EthereumNotifier");
            }
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            try
            {
                _cts.Cancel();
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
                try
                {
                    while (true)
                    {
                        foreach (var (address, subscription) in _subscriptions)
                        {
                            await RequestLimitControl
                                .Wait(_cts.Token)
                                .ConfigureAwait(false);

                            var requestBuilder = new StringBuilder("api?module=account&action=txlist");
                            requestBuilder.Append("&address=");
                            requestBuilder.Append(address);
                            requestBuilder.Append("&tag=latest&page=1&startBlock=");
                            requestBuilder.Append(subscription.StartBlock);

                            var requestUri = requestBuilder.ToString();

                            var resultLength = await HttpHelper.GetAsyncResult<int>(
                                    baseUri: BaseUrl,
                                    requestUri: requestUri,
                                    responseHandler: (_, content) =>
                                    {
                                        _log.Information(
                                            "EthereumNotifier.RunBalanceChecker got from etherscan.io: {@Content}",
                                            content);
                                        var json = JsonConvert.DeserializeObject<JObject>(content);

                                        if (json.ContainsKey("status")
                                            && json["status"]!.ToString() != "1"
                                            && json["message"]?.ToString()?.Contains("NOTOK") == true
                                           )
                                        {
                                            _log.Warning("Status is NOTOK from Etherscan, response: {@Response}",
                                                json.ToString());
                                        }

                                        if (!json.ContainsKey("result")) return 0;

                                        var length = json["result"]!.Count();
                                        var blockNumber = length > 0
                                            ? json["result"]![length - 1]!["blockNumber"]!.Value<int>()
                                            : subscription.StartBlock;
                                        
                                        var updateResult = _subscriptions.TryUpdate(address,
                                            subscription with {StartBlock = blockNumber + 1},
                                            subscription
                                        );

                                        if (!updateResult)
                                        {
                                            _log.Warning(
                                                "Could not update start block of subscription for address {Address}",
                                                address);
                                        }

                                        return length;

                                    },
                                    cancellationToken: _cts.Token)
                                .ConfigureAwait(false);

                            if (resultLength.Value <= 0) continue;

                            try
                            {
                                subscription.Handler(address);
                            }
                            catch (Exception e)
                            {
                                _log.Error(e, "Caught error on ether balance updated handler call");
                            }
                        }

                        await Task.Delay(_transactionsDelay);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("EthereumNotifier.RunBalanceChecker canceled");
                }
                catch (Exception e)
                {
                    Log.Error(e, "EthereumNotifier.RunBalanceChecker caught error");
                }
            }, _cts.Token);
        }
    }
}
