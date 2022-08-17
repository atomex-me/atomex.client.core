using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Websocket.Client;

using Atomex.Client.Abstract;
using Atomex.Client.Common;
using Atomex.Client.Entities;
using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;
using Atomex.Common;
using Atomex.Cryptography.Abstract;
using Atomex.MarketData.Common;
using Atomex.MarketData.Entities;
using Error = Atomex.Common.Error;

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
        private const uint DEFAULT_AUTHENTICATION_ACCOUNT_INDEX = 0u;
        private const int HeartBeatIntervalInSec = 10;

        public event EventHandler<ServiceEventArgs> ServiceStatusChanged;
        public event EventHandler<ServiceErrorEventArgs> Error;
        public event EventHandler<OrderEventArgs> OrderUpdated;
        public event EventHandler<SwapEventArgs> SwapUpdated;
        public event EventHandler<QuotesEventArgs> QuotesUpdated;
        public event EventHandler<EntriesEventArgs> EntriesUpdated;
        public event EventHandler<SnapshotEventArgs> SnapshotUpdated;

        protected string AccountUserId { get; private set; }
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
        private readonly AtomexClientOptions _options;
        private readonly ILogger _log;
        private readonly AuthMessageSigner _authMessageSigner;

        public WebSocketAtomexClient(
            string authTokenBaseUrl,
            string exchangeUrl,
            string marketDataUrl,
            AuthMessageSigner authMessageSigner,
            AtomexClientOptions options = default,
            ILogger log = null)
        {
            _authTokenBaseUrl = authTokenBaseUrl ?? throw new ArgumentNullException(nameof(authTokenBaseUrl));
            _exchangeUrl = exchangeUrl ?? throw new ArgumentNullException(nameof(exchangeUrl));
            _marketDataUrl = marketDataUrl ?? throw new ArgumentNullException(nameof(marketDataUrl));
            _options = options ?? AtomexClientOptions.DefaultOptions;
            _authMessageSigner = authMessageSigner ?? throw new ArgumentNullException(nameof(authMessageSigner));
            _log = log;
        }

        public bool IsServiceConnected(Service service) =>
            service switch
            {
                Service.Exchange => _exchangeWs?.IsConnected ?? false,
                Service.MarketData => _marketDataWs?.IsConnected ?? false,
                _ => throw new ArgumentOutOfRangeException(nameof(service), service, null)
            };

        public async Task StartAsync()
        {
            _authToken = await AuthAsync()
                .ConfigureAwait(false);

            _log?.LogDebug($"Auth token: {_authToken}");

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
            _log?.LogDebug("Exchange client connected");

            if (IsTaskCompleted(_exchangeHeartBeatTask))
            {
                _log?.LogDebug("Run HeartBeat loop for Exchange service");

                _exchangeHeartBeatCts = new CancellationTokenSource();
                _exchangeHeartBeatTask = HeartBeatLoopAsync(_exchangeWs, _exchangeHeartBeatCts.Token);
            }

            ServiceStatusChanged?.Invoke(this, new ServiceEventArgs(Service.Exchange, ServiceStatus.Connected));

            if (_options.CancelOrdersAfterConnect)
                CancelAllOrders(symbol: null, side: null, forAllConnections: true);

            if (_options.CancelOrdersAfterDisconnect) // set orders auto cancel flag, after disconnect all orders will be canceled
                SetOrdersAutoCancel(autoCancel: true);

            ServiceStatusChanged?.Invoke(this, new ServiceEventArgs(Service.Exchange, ServiceStatus.Authenticated));
        }

        private void ExchangeDisconnected(object sender, EventArgs e)
        {
            _log?.LogDebug("Exchange client disconnected");

            if (!IsTaskCompleted(_exchangeHeartBeatTask))
            {
                try
                {
                    _log?.LogDebug("Cancel Exchange client heartbeat");
                    _exchangeHeartBeatCts.Cancel();
                }
                catch (OperationCanceledException)
                {
                    _log?.LogDebug("Exchange heartbeat loop canceled");
                }
            }

            ServiceStatusChanged?.Invoke(this, new ServiceEventArgs(Service.Exchange, ServiceStatus.Disconnected));
        }

        private void ExchangeOnMessage(object sender, ResponseMessage e)
        {
            if (e.MessageType == WebSocketMessageType.Text)
            {
                var response = JsonConvert.DeserializeObject<JObject>(e.Text);

                var @event = response["event"].Value<string>();

                switch (@event)
                {
                    case "pong": HandlePong(Service.Exchange); break;
                    case "error": HandleError(response, Service.Exchange); break;
                    case "order": HandleOrder(response); break;
                    case "swap": HandleSwap(response); break;
                    //case "orderSendReply": break;
                    //case "orderCancelReply": break;
                    //case "cancelAllOrdersReply": break;
                    //case "getOrderReply": break;
                    //case "getOrdersReply" break;
                    case "getSwapReply": HandleSwap(response); break;
                    //case "getSwapsReply": break;
                    //case "addRequisitesReply": break;
                    //case "setOrdersAutoCancelReply": break;
                };
            }
            else throw new NotImplementedException();
        }

        private void MarketDataConnected(object sender, EventArgs e)
        {
            _log?.LogDebug("MarketData client connected");

            if (IsTaskCompleted(_marketDataHeartBeatTask))
            {
                _log?.LogDebug("Run HeartBeat loop for MarketData service");

                _marketDataHeartBeatCts = new CancellationTokenSource();
                _marketDataHeartBeatTask = HeartBeatLoopAsync(_marketDataWs, _marketDataHeartBeatCts.Token);
            }

            ServiceStatusChanged?.Invoke(this, new ServiceEventArgs(Service.MarketData, ServiceStatus.Connected));
            ServiceStatusChanged?.Invoke(this, new ServiceEventArgs(Service.MarketData, ServiceStatus.Authenticated));
        }

        private void MarketDataDisconnected(object sender, EventArgs e)
        {
            _log?.LogDebug("MarketData client disconnected");

            if (!IsTaskCompleted(_marketDataHeartBeatTask))
            {
                try
                {
                    _log?.LogDebug("Cancel MarketData client heartbeat");
                    _marketDataHeartBeatCts.Cancel();
                }
                catch (OperationCanceledException)
                {
                    _log?.LogDebug("MarketData heart beat loop canceled");
                }
            }

            ServiceStatusChanged?.Invoke(this, new ServiceEventArgs(Service.MarketData, ServiceStatus.Disconnected));
        }

        private void MarketDataOnMessage(object sender, ResponseMessage e)
        {
            if (e.MessageType == WebSocketMessageType.Text)
            {
                var response = JsonConvert.DeserializeObject<JObject>(e.Text);

                var @event = response["event"].Value<string>();

                switch (@event)
                {
                    case "pong": HandlePong(Service.MarketData); break;
                    case "error": HandleError(response, Service.MarketData); break;
                    case "topOfBook": HandleTopOfBook(response); break;
                    case "entries": HandleEntries(response); break;
                    case "snapshot": HandleSnapshot(response); break;
                };
            }
            else throw new NotImplementedException();
        }

        public async void OrderSendAsync(Order order)
        {
            try
            {
                _log?.LogInformation("Sending the {OrderId} [{OrderClientId}] order...", order.Id, order.ClientOrderId);

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
                        requisites = new
                        {
                            baseCurrencyContract = order.BaseCurrencyContract,
                            quoteCurrencyContract = order.QuoteCurrencyContract
                            // secretHash =,
                            // receivingAddress =,
                            // refundAddress =,
                            // rewardForRedeem =,
                            // lockTime =,
                        },
                        //proofOfFunds =
                    },
                    requestId = 0
                };

                _exchangeWs.Send(JsonConvert.SerializeObject(request));
            }
            catch (OperationCanceledException)
            {
                _log?.LogDebug("The {TaskName} task has been canceled", nameof(OrderSendAsync));
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "Sending the {OrderId} [{OrderClientId}] order is failed for the {UserId} user", AccountUserId, order.Id, order.ClientOrderId);
            }
        }

        public void OrderCancelAsync(long id, string symbol, Side side)
        {
            var request = new
            {
                method = "orderCancel",
                data = new
                {
                    id     = id,
                    symbol = symbol,
                    side   = (int)side
                },
                requestId = 0
            };

            _exchangeWs.Send(JsonConvert.SerializeObject(request));
        }

        public void SubscribeToMarketData(SubscriptionType type)
        {
            var stream = type switch
            {
                SubscriptionType.TopOfBook   => "topOfBook",
                SubscriptionType.DepthTwenty => "orderBook",
                SubscriptionType.OrderLog    => throw new NotSupportedException("Full OrderLog stream not supported"),
                _ => throw new NotSupportedException($"Type {type} not supported"),
            };

            var request = new
            {
                method    = "subscribe",
                data      = stream,
                requestId = 0
            };

            _marketDataWs.Send(JsonConvert.SerializeObject(request));
        }

        public void SwapInitiateAsync(
            long id,
            byte[] secretHash,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress,
            ulong lockTime)
        {
            var request = new
            {
                method = "addRequisites",
                data = new
                {
                    id               = id,
                    secretHash       = secretHash.ToHexString(),
                    receivingAddress = toAddress,
                    refundAddress    = refundAddress,
                    rewardForRedeem  = rewardForRedeem,
                    lockTime         = lockTime
                },
                requestId = 0
            };

            _exchangeWs.Send(JsonConvert.SerializeObject(request));
        }

        public void SwapAcceptAsync(
            long id,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress,
            ulong lockTime)
        {
            var request = new
            {
                method = "addRequisites",
                data = new
                {
                    id               = id,
                    receivingAddress = toAddress,
                    refundAddress    = refundAddress,
                    rewardForRedeem  = rewardForRedeem,
                    lockTime         = lockTime
                },
                requestId = 0
            };

            _exchangeWs.Send(JsonConvert.SerializeObject(request));
        }

        public void SwapStatusAsync(
            string requestId,
            long swapId)
        {
            var request = new
            {
                method = "getSwap",
                data = new
                {
                    id = swapId,
                },
                requestId = 0
            };

            _exchangeWs.Send(JsonConvert.SerializeObject(request));
        }

        private void SetOrdersAutoCancel(bool autoCancel)
        {
            var request = new
            {
                method = "setOrdersAutoCancel",
                data = new
                {
                    autoCancel = autoCancel
                },
                requestId = 0
            };

            _exchangeWs.Send(JsonConvert.SerializeObject(request));
        }

        private void CancelAllOrders(
            string symbol = null,
            Side? side = null,
            bool forAllConnections = false)
        {
            var request = new
            {
                method = "cancelAllOrders",
                data = new
                {
                    symbol = symbol,
                    side = side == null ? "All" : side.ToString(),
                    cancelForAllConnections = forAllConnections
                },
                requestId = 0
            };

            _exchangeWs.Send(JsonConvert.SerializeObject(request));
        }

        private async Task<string> AuthAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                const string signingAlgorithm = "Sha256WithEcdsa:BtcMsg";

                var timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var message = "Hello, Atomex!";
                var messageToSignBytes = Encoding.UTF8.GetBytes($"{message}{timeStamp}");
                var signingMessagePayload = HashAlgorithm.Sha256.Hash(
                        BitcoinUtils.FormatMessageForSigning(messageToSignBytes),
                        iterations: 2
                    );

                var (publicKey, signature) = await _authMessageSigner
                    .Invoke(signingMessagePayload, signingAlgorithm)
                    .ConfigureAwait(false);

                var body = new
                {
                    timeStamp = timeStamp,
                    message   = message,
                    publicKey = Hex.ToHexString(publicKey),
                    signature = Hex.ToHexString(signature),
                    algorithm = signingAlgorithm
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
                    _log?.LogError($"Can't get Auth token. Error code: {response.StatusCode}");
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
                _log?.LogError($"Get Auth token error: {ex.Message}");
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
                    _log?.LogDebug("HeartBeat loop canceled");
                }
                catch (Exception e)
                {
                    _log?.LogError(e, "Error while sending heartbeat");
                }
            }

            _log?.LogDebug("Heartbeat stopped");
        }

        private static bool IsTaskCompleted(Task task) =>
            task == null ||
            task.IsCompleted ||
            task.IsCanceled ||
            task.IsFaulted;

        private void HandlePong(Service service)
        {
            _log?.LogTrace($"Pong received from {service}");
        }

        private void HandleError(JObject response, Service service)
        {
            Error?.Invoke(
                sender: this,
                e: new ServiceErrorEventArgs(
                    service: service,
                    error: new Error(
                        code: Errors.RequestError,
                        description: response["data"].Value<string>())));
        }

        private async void HandleOrder(JObject response)
        {
            try
            {
                var totalQty = 0m;
                var totalAmount = 0m;

                var data = response["data"] as JObject;

                if (data.ContainsKey("trades"))
                {
                    foreach (var trade in data["trades"])
                    {
                        var price = trade["price"].Value<decimal>();
                        var qty = trade["qty"].Value<decimal>();

                        totalQty += qty;
                        totalAmount += price * qty;
                    }
                }

                var order = new Order
                {
                    Id            = data["id"].Value<long>(),
                    ClientOrderId = data["clientOrderId"].Value<string>(),
                    Symbol        = data["symbol"].Value<string>(),
                    Side          = (Side)Enum.Parse(typeof(Side), data["side"].Value<string>()),
                    TimeStamp     = data["timeStamp"].Value<DateTime>(),
                    Price         = data["price"].Value<decimal>(),
                    Qty           = data["qty"].Value<decimal>(),
                    LeaveQty      = data["leaveQty"].Value<decimal>(),
                    LastPrice     = totalQty != 0 ? totalAmount / totalQty : 0, // currently average price used
                    LastQty       = totalQty,                                   // currently total qty used
                    Type          = (OrderType)Enum.Parse(typeof(OrderType), data["type"].Value<string>()),
                    Status        = (OrderStatus)Enum.Parse(typeof(OrderStatus), data["status"].Value<string>())
                };

                OrderUpdated?.Invoke(
                    sender: this,
                    e: new OrderEventArgs(order));
            }
            catch (OperationCanceledException)
            {
                _log?.LogDebug("The {TaskName} task has been canceled", nameof(HandleOrder));
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "Order handling failed for the {UserId} user", AccountUserId);
            }
        }

        public enum PartyStatus
        {
            Created,
            Involved,
            PartiallyInitiated,
            Initiated,
            Redeemed,
            Refunded,
            Lost,
            Jackpot
        }

        private void HandleSwap(JObject response)
        {
            var data = response["data"] as JObject;

            var IsInitiator = data["isInitiator"].Value<bool>();

            var status = SwapStatus.Empty;
            var userStatus = (PartyStatus)Enum.Parse(typeof(PartyStatus), data["user"]["status"].Value<string>());
            var partyStatus = (PartyStatus)Enum.Parse(typeof(PartyStatus), data["counterParty"]["status"].Value<string>());

            if (userStatus > PartyStatus.Created)
                status |= IsInitiator
                    ? SwapStatus.Initiated
                    : SwapStatus.Accepted;

            if (partyStatus > PartyStatus.Created)
                status |= IsInitiator
                    ? SwapStatus.Accepted
                    : SwapStatus.Initiated;

            var secretHash = data["secretHash"].Value<string>();
            var swap = new Swap
            {
                Id           = data["id"].Value<long>(),
                Status       = status,
                SecretHash   = !string.IsNullOrWhiteSpace(secretHash) ? Hex.FromString(secretHash) : null,
                TimeStamp    = data["timeStamp"].Value<DateTime>(),
                OrderId      = data["user"]?["trades"]?[0]?["orderId"]?.Value<long>() ?? 0,
                Symbol       = data["symbol"].Value<string>(),
                Side         = (Side)Enum.Parse(typeof(Side), data["side"].Value<string>()),
                Price        = data["price"].Value<decimal>(),
                Qty          = data["qty"].Value<decimal>(),
                IsInitiative = IsInitiator,

                ToAddress       = data["user"]?["requisites"]?["receivingAddress"]?.Value<string>(),
                RewardForRedeem = data["user"]?["requisites"]?["rewardForRedeem"]?.Value<decimal>() ?? 0,
                RefundAddress   = data["user"]?["requisites"]?["refundAddress"]?.Value<string>(),

                PartyAddress         = data["counterParty"]?["requisites"]?["receivingAddress"]?.Value<string>(),
                PartyRewardForRedeem = data["counterParty"]?["requisites"]?["rewardForRedeem"]?.Value<decimal>() ?? 0,
                PartyRefundAddress   = data["counterParty"]?["requisites"]?["refundAddress"]?.Value<string>(),
            };

            SwapUpdated?.Invoke(
                sender: this,
                e: new SwapEventArgs(swap));
        }

        private void HandleTopOfBook(JObject response)
        {
            var quotes = new List<Quote>();

            foreach (var quote in response["data"])
            {
                quotes.Add(new Quote
                {
                    Ask       = quote["ask"]?.Value<decimal>() ?? 0m,
                    Bid       = quote["bid"]?.Value<decimal>() ?? 0m,
                    Symbol    = quote["symbol"]?.Value<string>(),
                    TimeStamp = quote["timeStamp"]?.Value<long>().ToUtcDateTimeFromMs() ?? DateTime.MinValue
                });
            }

            _log?.LogTrace("Quotes: {@quotes}", quotes);

            QuotesUpdated?.Invoke(this, new QuotesEventArgs(quotes));
        }

        private void HandleSnapshot(JObject response)
        {
            if (response["data"] == null)
            {
                _log.LogWarning("Empty snapshot received");
                return;
            }

            var entries = new List<Entry>();

            foreach (var entry in response["data"]["entries"])
            {
                entries.Add(new Entry
                {
                    Price         = entry["price"].Value<decimal>(),
                    QtyProfile    = entry["qtyProfile"].ToObject<List<decimal>>(),
                    Side          = (Side)Enum.Parse(typeof(Side), entry["side"].Value<string>()),
                });
            }

            var snapshot = new Snapshot
            {
                Entries           = entries,
                LastTransactionId = response["data"]["updateId"].Value<long>(),
                Symbol            = response["data"]["symbol"].Value<string>()
            };

            _log?.LogTrace("Snapshot: {@snapshot}", snapshot);

            SnapshotUpdated?.Invoke(this, new SnapshotEventArgs(snapshot));
        }

        private void HandleEntries(JObject response)
        {
            var entries = new List<Entry>();

            foreach (var entry in response["data"])
            {
                entries.Add(new Entry
                {
                    TransactionId = entry["updateId"].Value<long>(),
                    Symbol        = entry["symbol"].Value<string>(),
                    Price         = entry["price"].Value<decimal>(),
                    QtyProfile    = entry["qtyProfile"].ToObject<List<decimal>>(),
                    Side          = (Side)Enum.Parse(typeof(Side), entry["side"].Value<string>()),
                });
            }

            EntriesUpdated?.Invoke(this, new EntriesEventArgs(entries));
        }
    }
}