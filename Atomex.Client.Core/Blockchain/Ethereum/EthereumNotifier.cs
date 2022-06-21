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
    internal record Subscription(Action<string> Handler, long StartBlock);

    public class EthereumNotifier : IEthereumNotifier
    {
        public string BaseUrl { get; }
        private readonly ILogger _log;
        private readonly ConcurrentDictionary<string, Subscription> _subscriptions = new();

        private bool _isRunning;
        private CancellationTokenSource _cts;

        private readonly TimeSpan _transactionsDelay = TimeSpan.FromSeconds(15);
        private const int MinDelayBetweenRequestMs = 7000;
        private static readonly RequestLimitControl RequestLimitControl 
            = new(MinDelayBetweenRequestMs);

        private long _lastBlockNumber = 7096734; // Value that is bigger than 0 and definitely less then current block number of any Ether network. 
        private const string ApiKey = "YUIREI3IDPD48WD6ZB9M1SYNAGPYEKAZ8H"; // Free ApiKey to increase rate limits

        public EthereumNotifier(string baseUrl, ILogger log)
        {
            BaseUrl = baseUrl;
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

                await GetLastBlockNumber();

                RunBalanceChecker();
            }
            catch (OperationCanceledException)
            {
                Log.Debug("EthereumNotifier.StartAsync canceled");
            }
            catch (Exception e)
            {
                _log.Error(e, "Error on starting EthereumNotifier");
            }
        }

        public Task StopAsync()
        {
            if (!_isRunning)
            {
                return Task.CompletedTask;
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

            return Task.CompletedTask;
        }

        public void SubscribeOnBalanceUpdate(string address, Action<string> handler)
        {
            _subscriptions.AddOrUpdate(address, 
                (_) => new Subscription(handler, _lastBlockNumber), 
                (_, sub) => sub with { Handler = handler }
            );
        }

        public void SubscribeOnBalanceUpdate(IEnumerable<string> addresses, Action<string> handler)
        {
            var subscription = new Subscription(handler, _lastBlockNumber);

            foreach (var address in addresses) 
            {
                _subscriptions.AddOrUpdate(address,
                    subscription, 
                    (_, sub) => sub with { Handler = handler }
                );
            }
        }

        private void RunBalanceChecker()
        {
            Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await BalanceCheckerLoop().ConfigureAwait(false);
                        await Task.Delay(_transactionsDelay).ConfigureAwait(false);
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

        private async Task BalanceCheckerLoop()
        {
            foreach (var (address, subscription) in _subscriptions)
            {
                await RequestLimitControl
                    .Wait(_cts.Token)
                    .ConfigureAwait(false);

                var requestBuilder = new StringBuilder("api?module=account&action=txlist");
                requestBuilder.Append("&address=");
                requestBuilder.Append(address);
                requestBuilder.Append("&apikey=");
                requestBuilder.Append(ApiKey);
                requestBuilder.Append("&tag=latest&page=1&startBlock=");
                requestBuilder.Append(subscription.StartBlock);

                var requestUri = requestBuilder.ToString();

                var resultLength = await HttpHelper.GetAsyncResult<int>(
                        baseUri: BaseUrl,
                        requestUri: requestUri,
                        responseHandler: (_, content) =>
                        {
                            var json = JsonConvert.DeserializeObject<JObject>(content);

                            if (json.ContainsKey("status")
                                && json["status"]!.ToString() != "1"
                                && json["message"]?.ToString()?.Contains("NOTOK") == true
                               )
                            {
                                _log.Warning("Status is NOTOK from Etherscan, response: {@Response}",
                                    json.ToString());
                                return null;
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


                if (resultLength == null)
                {
                    Log.Error("Error while getting txlist for ether address {@Address}", address);
                    await Task.Delay(_transactionsDelay.Multiply(4));

                    continue;
                }

                if (resultLength.HasError)
                {
                    Log.Error(
                        "Error while getting txlist for ether address {@Address} with code {@code} and description {@description}",
                        address,
                        resultLength.Error.Code,
                        resultLength.Error.Description);

                    continue;
                }

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
        }

        private async Task GetLastBlockNumber()
        {
            const string requestUri = $"api?module=proxy&action=eth_blockNumber&apikey={ApiKey}";

            await RequestLimitControl
                .Wait(_cts.Token)
                .ConfigureAwait(false);

            var lastBlockResult = await HttpHelper.GetAsyncResult(
                    baseUri: BaseUrl,
                    requestUri: requestUri,
                    responseHandler: (_, content) =>
                    {
                        var json = JsonConvert.DeserializeObject<JObject>(content);
                        var blockNumber = _lastBlockNumber;
                        if (json != null
                            && !json.ContainsKey("status")
                            && json.ContainsKey("result"))
                        {
                            blockNumber = long.Parse(json["result"]!.ToString()[2..],
                                System.Globalization.NumberStyles.HexNumber);
                        }

                        return new Result<long>(blockNumber);
                    },
                    cancellationToken: _cts.Token)
                .ConfigureAwait(false);

            if (lastBlockResult == null)
            {
                Log.Error("Connection error while getting latest block number");
                return;
            }

            if (lastBlockResult.HasError)
            {
                Log.Error(
                    "Error while getting last block number with code {@code} and description {@description}",
                    lastBlockResult.Error.Code,
                    lastBlockResult.Error.Description);

                return;
            }

            _lastBlockNumber = lastBlockResult.Value;
        }
    }
}
