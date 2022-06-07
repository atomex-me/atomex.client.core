using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Websocket.Client;

using Atomex.Common;
using Atomex.Core;
using Atomex.MarketData;
using Atomex.MarketData.Abstract;
using Atomex.Services.Abstract;
using Atomex.Swaps;
using Atomex.Wallet.Abstract;
using Atomex.Web;

namespace Atomex.Services
{
    public class AuthTokenResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("token")]
        public string Token { get; set; }
        [JsonProperty("expires")]
        public long Expires { get; set; }
    }

    public class WebSocketAtomexClient : IAtomexClient
    {
        private const int HeartBeatIntervalInSec = 10;

        public event EventHandler<AtomexClientServiceEventArgs> ServiceConnected;
        public event EventHandler<AtomexClientServiceEventArgs> ServiceDisconnected;
        public event EventHandler<AtomexClientServiceEventArgs> ServiceAuthenticated;
        public event EventHandler<AtomexClientErrorEventArgs> Error;
        public event EventHandler<OrderEventArgs> OrderReceived;
        public event EventHandler<SwapEventArgs> SwapReceived;
        public event EventHandler<MarketDataEventArgs> QuotesUpdated;

        public IAccount Account { get; private set; }
        public IMarketDataRepository MarketDataRepository { get; private set; }

        private readonly string _authTokenBaseUrl;
        private readonly string _exchangeUrl;
        private readonly string _marketDataUrl;
        private WebSocketClient _exchangeWs;
        private WebSocketClient _marketDataWs;
        private string _authToken;
        private Task _exchangeHeartBeatTask;
        private Task _marketDataHeartBeatTask;
        private CancellationTokenSource _exchangeHeartBeatCts;
        private CancellationTokenSource _marketDataHeartBeatCts;

        public WebSocketAtomexClient(
            string authTokenBaseUrl,
            string exchangeUrl,
            string marketDataUrl,
            IAccount account)
        {
            _authTokenBaseUrl = authTokenBaseUrl ?? throw new ArgumentNullException(nameof(authTokenBaseUrl));
            _exchangeUrl = exchangeUrl ?? throw new ArgumentNullException(nameof(exchangeUrl));
            _marketDataUrl = marketDataUrl ?? throw new ArgumentNullException(nameof(marketDataUrl));
            Account = account ?? throw new ArgumentNullException(nameof(account));
            MarketDataRepository = new MarketDataRepository();
        }

        public bool IsServiceConnected(AtomexClientService service) =>
            service switch
            {
                AtomexClientService.Exchange => _exchangeWs.IsConnected,
                AtomexClientService.MarketData => _marketDataWs.IsConnected,
                AtomexClientService.All => _exchangeWs.IsConnected && _marketDataWs.IsConnected,
                _ => throw new ArgumentOutOfRangeException(nameof(service), service, null)
            };

        public async Task StartAsync()
        {
            _authToken = await AuthAsync()
                .ConfigureAwait(false);

            var authHeaders = new HttpRequestHeaders
            {
                new KeyValuePair<string, IEnumerable<string>>("Authorization", new string[] { $"Bearer {_authToken}" }),
                new KeyValuePair<string, IEnumerable<string>>("Content-Type", new string[] { "application/json" })
            };

            _exchangeWs = new WebSocketClient(_exchangeUrl, authHeaders);

            _exchangeWs.Connected    += ExchangeConnected;
            _exchangeWs.Disconnected += ExchangeDisconnected;
            _exchangeWs.OnMessage    += ExchangeOnMessage;

            _marketDataWs = new WebSocketClient(_marketDataUrl, authHeaders);

            _marketDataWs.Connected    += MarketDataConnected;
            _marketDataWs.Disconnected += MarketDataDisconnected;
            _marketDataWs.OnMessage    += MarketDataOnMessage;

            await Task.WhenAll(
                    _exchangeWs.ConnectAsync(),
                    _marketDataWs.ConnectAsync())
                .ConfigureAwait(false);
        }

        public Task StopAsync()
        {
            return Task.WhenAll(
                _exchangeWs.CloseAsync(),
                _marketDataWs.CloseAsync());
        }

        private void ExchangeConnected(object sender, EventArgs e)
        {
            Log.Debug("Exchange client connected");

            if (IsTaskCompleted(_exchangeHeartBeatTask))
            {
                Log.Debug("Run HeartBeat loop for Exchange service");

                _exchangeHeartBeatCts = new CancellationTokenSource();
                _exchangeHeartBeatTask = HeartBeatLoopAsync(_exchangeWs, _exchangeHeartBeatCts.Token);
            }

            ServiceConnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));
        }

        private void ExchangeDisconnected(object sender, EventArgs e)
        {
            Log.Debug("Exchange client disconnected");

            if (!IsTaskCompleted(_exchangeHeartBeatTask))
            {
                try
                {
                    Log.Debug("Cancel Exchange client heartbeat");
                    _exchangeHeartBeatCts.Cancel();
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Exchange heartbeat loop canceled");
                }
            }

            ServiceDisconnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));
        }

        private void ExchangeOnMessage(object sender, ResponseMessage e)
        {
            if (e.MessageType == WebSocketMessageType.Text)
            {
                var response = JsonConvert.DeserializeObject<JObject>(e.Text);

                var @event = response["event"].Value<string>();

                switch (@event)
                {
                    case "pong": break;
                    case "error": break;
                    case "order": break;
                    case "swap": break;
                    //case "orderSendReply": break;
                    //case "orderCancelReply": break;
                    //case "getOrderReply": break;
                    //case "getOrdersReply" break;
                    //case "getSwapReply": break;
                    //case "getSwapsReply": break;
                    //case "addRequisitesReply": break;
                };
            }
            else throw new NotImplementedException();
        }

        private void MarketDataConnected(object sender, EventArgs e)
        {
            Log.Debug("MarketData client connected");

            if (IsTaskCompleted(_marketDataHeartBeatTask))
            {
                Log.Debug("Run HeartBeat loop for MarketData service");

                _marketDataHeartBeatCts = new CancellationTokenSource();
                _marketDataHeartBeatTask = HeartBeatLoopAsync(_marketDataWs, _marketDataHeartBeatCts.Token);
            }

            ServiceConnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
        }

        private void MarketDataDisconnected(object sender, EventArgs e)
        {
            Log.Debug("MarketData client disconnected");

            if (!IsTaskCompleted(_marketDataHeartBeatTask))
            {
                try
                {
                    Log.Debug("Cancel MarketData client heartbeat");
                    _marketDataHeartBeatCts.Cancel();
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("MarketData heart beat loop canceled");
                }
            }

            ServiceDisconnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
        }

        private void MarketDataOnMessage(object sender, ResponseMessage e)
        {
            if (e.MessageType == WebSocketMessageType.Text)
            {
                var response = JsonConvert.DeserializeObject<JObject>(e.Text);

                var @event = response["event"].Value<string>();

                switch (@event)
                {
                    case "pong": break;
                    case "error": break;
                    case "topOfBook": break;
                    case "entries": break;
                    case "snapshot": break;
                };
            }
            else throw new NotImplementedException();
        }

        public void OrderSendAsync(Order order)
        {
            var request = new
            {
                method = "orderSend",
                data = new
                {
                    clientOrderId = order.ClientOrderId,
                    symbol = order.Symbol,
                    price = order.Price,
                    qty = order.Qty,
                    side = order.Side,
                    type = (int)order.Type,
                    //proofOfFunds =
                    //requisites = 
                },
                requestId = 0
            };

            _exchangeWs.Send(JsonConvert.SerializeObject(request));
        }

        public void OrderCancelAsync(long id, string symbol, Side side)
        {
            var request = new
            {
                method = "orderCancel",
                data = new
                {
                    id = id,
                    symbol = symbol,
                    side = (int)side
                },
                requestId = 0
            };

            _exchangeWs.Send(JsonConvert.SerializeObject(request));
        }

        public void SubscribeToMarketData(SubscriptionType type)
        {
            var stream = type switch
            {
                SubscriptionType.TopOfBook => "topOfBook",
                SubscriptionType.DepthTwenty => "orderBook",
                SubscriptionType.OrderLog => throw new NotSupportedException("Full OrderLog stream not supported"),
                _ => throw new NotSupportedException($"Type {type} not supported"),
            };

            var request = new
            {
                method = "subscribe",
                data = stream,
                requestId = 0
            };

            _marketDataWs.Send(JsonConvert.SerializeObject(request));
        }

        public MarketDataOrderBook GetOrderBook(string symbol) =>
            MarketDataRepository?.OrderBookBySymbol(symbol);

        public MarketDataOrderBook GetOrderBook(Symbol symbol) =>
            MarketDataRepository?.OrderBookBySymbol(symbol.Name);

        public Quote GetQuote(Symbol symbol) =>
            MarketDataRepository?.QuoteBySymbol(symbol.Name);

        public void SwapInitiateAsync(
            long id,
            byte[] secretHash,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress)
        {
            throw new NotImplementedException();
        }

        public void SwapAcceptAsync(
            long id,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress)
        {
            throw new NotImplementedException();
        }

        public void SwapStatusAsync(
            string requestId,
            long swapId)
        {
            throw new NotImplementedException();
        }

        private async Task<string> AuthAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var securePublicKey = Account.Wallet.GetServicePublicKey(index: 0);
                var publicKey = securePublicKey.ToUnsecuredBytes();

                var timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var message = "Hello, Atomex!";
                var messageToSignBytes = Encoding.UTF8.GetBytes($"{message}{timeStamp}");
                var hash = BitcoinSignHelper.MessageHash(messageToSignBytes);

                var signature = await Account.Wallet
                    .SignByServiceKeyAsync(hash, keyIndex: 0, cancellationToken)
                    .ConfigureAwait(false);

                var body = new
                {
                    timeStamp = timeStamp,
                    message = message,
                    publicKey = Hex.ToHexString(publicKey),
                    signature = Hex.ToHexString(signature),
                    algorithm = "Sha256WithEcdsa:BtcMsg"
                };

                var content = new StringContent(
                    content: JsonConvert.SerializeObject(body),
                    encoding: Encoding.UTF8,
                    mediaType: "application/json");

                using var response= await HttpHelper
                    .PostAsync(
                        baseUri: _authTokenBaseUrl,
                        relativeUri: "token",
                        content: content,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    Log.Error($"Can't get Auth token. Error code: {response.StatusCode}");
                    return null;
                }

                var responseContent = await response.Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                return JsonConvert
                    .DeserializeObject<AuthTokenResponse>(responseContent)
                    ?.Token;
            }
            catch (Exception ex)
            {
                Log.Error($"Get Auth token error: {ex.Message}");
                return null;
            }
        }

        private async Task HeartBeatLoopAsync(
            WebSocketClient ws,
            CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var ping = new
                    {
                        method = "ping",
                        requestId = 0
                    };

                    ws.Send(JsonConvert.SerializeObject(ping));

                    await Task.Delay(TimeSpan.FromSeconds(HeartBeatIntervalInSec), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("HeartBeat loop canceled");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error while sending heartbeat");
                }
            }

            Log.Debug("Heartbeat stopped");
        }

        private static bool IsTaskCompleted(Task task) =>
            task == null ||
            task.IsCompleted ||
            task.IsCanceled ||
            task.IsFaulted;
    }
}