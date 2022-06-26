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

using Atomex.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.MarketData;
using Atomex.MarketData.Abstract;
using Atomex.Services.Abstract;
using Atomex.Swaps;
using Atomex.Swaps.Abstract;
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
        private const uint DEFAULT_AUTHENTICATION_ACCOUNT_INDEX = 0u;
        private const int HeartBeatIntervalInSec = 10;

        public event EventHandler<AtomexClientServiceEventArgs> ServiceConnected;
        public event EventHandler<AtomexClientServiceEventArgs> ServiceDisconnected;
        public event EventHandler<AtomexClientServiceEventArgs> ServiceAuthenticated;
        public event EventHandler<AtomexClientErrorEventArgs> Error;
        public event EventHandler<OrderEventArgs> OrderUpdated;
        public event EventHandler<SwapEventArgs> SwapUpdated;
        public event EventHandler<MarketDataEventArgs> QuotesUpdated;

        public IAccount Account { get; init; }
        public IMarketDataRepository MarketDataRepository { get; init; }
        protected string AccountUserId => _accountUserIdLazy.Value;
        private readonly ISymbolsProvider _symbolsProvider;
        private readonly string _authTokenBaseUrl;
        private readonly string _exchangeUrl;
        private readonly string _marketDataUrl;
        private readonly Lazy<string> _accountUserIdLazy;
        private WebSocketClient _exchangeWs;
        private WebSocketClient _marketDataWs;
        private string _authToken;
        private Task _exchangeHeartBeatTask;
        private Task _marketDataHeartBeatTask;
        private CancellationTokenSource _exchangeHeartBeatCts;
        private CancellationTokenSource _marketDataHeartBeatCts;
        private readonly AtomexClientOptions _options;

        public WebSocketAtomexClient(
            string authTokenBaseUrl,
            string exchangeUrl,
            string marketDataUrl,
            IAccount account,
            ISymbolsProvider symbolsProvider,
            AtomexClientOptions options = default)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));

            _authTokenBaseUrl = authTokenBaseUrl ?? throw new ArgumentNullException(nameof(authTokenBaseUrl));
            _exchangeUrl      = exchangeUrl ?? throw new ArgumentNullException(nameof(exchangeUrl));
            _marketDataUrl    = marketDataUrl ?? throw new ArgumentNullException(nameof(marketDataUrl));
            _symbolsProvider  = symbolsProvider ?? throw new ArgumentNullException(nameof(symbolsProvider));
            _options          = options ?? AtomexClientOptions.DefaultOptions;

            MarketDataRepository = new MarketDataRepository();
            _accountUserIdLazy = new Lazy<string>(() => Account.GetUserId(DEFAULT_AUTHENTICATION_ACCOUNT_INDEX));
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

            Log.Debug($"Auth token: {_authToken}");

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

            if (_options.CancelOrdersAfterConnect)
                CancelAllOrders(symbol: null, side: null, forAllConnections: true);

            if (_options.CancelOrdersAfterDisconnect) // set orders auto cancel flag, after disconnect all orders will be canceled
                SetOrdersAutoCancel(autoCancel: true);

            ServiceAuthenticated?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));
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
                    case "pong": HandlePong(AtomexClientService.Exchange); break;
                    case "error": HandleError(response, AtomexClientService.Exchange); break;
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
            Log.Debug("MarketData client connected");

            if (IsTaskCompleted(_marketDataHeartBeatTask))
            {
                Log.Debug("Run HeartBeat loop for MarketData service");

                _marketDataHeartBeatCts = new CancellationTokenSource();
                _marketDataHeartBeatTask = HeartBeatLoopAsync(_marketDataWs, _marketDataHeartBeatCts.Token);
            }

            ServiceConnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
            ServiceAuthenticated?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
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
                    case "pong": HandlePong(AtomexClientService.MarketData); break;
                    case "error": HandleError(response, AtomexClientService.MarketData); break;
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
                order.ClientOrderId = GenerateOrderClientId();

                Log.Information("Sending the {OrderId} [{OrderClientId}] order...", order.Id, order.ClientOrderId);

                await Account.UpsertOrderAsync(order)
                    .ConfigureAwait(false);

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
                            baseCurrencyContract = GetSwapContract(order.Symbol.BaseCurrency()),
                            quoteCurrencyContract = GetSwapContract(order.Symbol.QuoteCurrency()),
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
                Log.Debug("The {TaskName} task has been canceled", nameof(OrderSendAsync));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Sending the {OrderId} [{OrderClientId}] order is failed for the {UserId} user", AccountUserId, order.Id, order.ClientOrderId);
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
                    lockTime         = CurrencySwap.DefaultInitiatorLockTimeInSeconds
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
            string refundAddress)
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
                    lockTime         = CurrencySwap.DefaultAcceptorLockTimeInSeconds
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
                using var securePublicKey = Account.Wallet.GetServicePublicKey(index: DEFAULT_AUTHENTICATION_ACCOUNT_INDEX);
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
                    message   = message,
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

        private void HandlePong(AtomexClientService service)
        {
            Log.Verbose($"Pong received from {service}");
        }

        private void HandleError(JObject response, AtomexClientService service)
        {
            Error?.Invoke(
                sender: this,
                e: new AtomexClientErrorEventArgs(
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

                // remove canceled orders without trades from local db if StoreCanceledOrders options is true
                if (order.Status == OrderStatus.Canceled && order.LastQty == 0 && !_options.StoreCanceledOrders)
                {
                    await Account
                        .RemoveOrderByIdAsync(order.Id)
                        .ConfigureAwait(false);
                }
                else
                {
                    await Account
                        .UpsertOrderAsync(order)
                        .ConfigureAwait(false);
                }

                OrderUpdated?.Invoke(
                    sender: this,
                    e: new OrderEventArgs(order));
            }
            catch (OperationCanceledException)
            {
                Log.Debug("The {TaskName} task has been canceled", nameof(HandleOrder));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Order handling failed for the {UserId} user", AccountUserId);
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
                    Ask       = quote["Ask"].Value<decimal>(),
                    Bid       = quote["Bid"].Value<decimal>(),
                    Symbol    = quote["Symbol"].Value<string>(),
                    TimeStamp = quote["TimeStamp"].Value<long>().ToUtcDateTimeFromMs()
                });
            }

            Log.Verbose("Quotes: {@quotes}", quotes);

            MarketDataRepository.ApplyQuotes(quotes);

            var symbolsIds = new HashSet<string>();

            foreach (var quote in quotes)
                if (!symbolsIds.Contains(quote.Symbol))
                    symbolsIds.Add(quote.Symbol);

            foreach (var symbolId in symbolsIds)
            {
                var symbol = _symbolsProvider
                    .GetSymbols(Account.Network)
                    .GetByName(symbolId);

                if (symbol != null)
                    QuotesUpdated?.Invoke(this, new MarketDataEventArgs(symbol));
            }
        }

        private void HandleSnapshot(JObject response)
        {
            foreach (var s in response["data"])
            {
                var entries = new List<Entry>();

                foreach (var entry in s["Entries"])
                {
                    entries.Add(new Entry
                    {
                        Price         = entry["Price"].Value<decimal>(),
                        QtyProfile    = entry["QtyProfile"].ToObject<List<decimal>>(),
                        Side          = (Side)Enum.Parse(typeof(Side), entry["side"].Value<string>()),
                    });
                }

                var snapshot = new Snapshot
                {
                    Entries           = entries,
                    LastTransactionId = s["UpdateId"].Value<long>(),
                    Symbol            = s["Symbol"].Value<string>()
                };

                Log.Verbose("Snapshot: {@snapshot}", snapshot);

                MarketDataRepository.ApplySnapshot(snapshot);

                var symbol = _symbolsProvider
                    .GetSymbols(Account.Network)
                    .GetByName(snapshot.Symbol);

                if (symbol != null)
                    QuotesUpdated?.Invoke(this, new MarketDataEventArgs(symbol));
            }
        }

        private void HandleEntries(JObject response)
        {
            var entries = new List<Entry>();

            foreach (var entry in response["data"])
            {
                entries.Add(new Entry
                {
                    TransactionId = entry["UpdateId"].Value<long>(),
                    Symbol        = entry["Symbol"].Value<string>(),
                    Price         = entry["Price"].Value<decimal>(),
                    QtyProfile    = entry["QtyProfile"].ToObject<List<decimal>>(),
                    Side          = (Side)Enum.Parse(typeof(Side), entry["side"].Value<string>()),
                });
            }

            MarketDataRepository.ApplyEntries(entries);
        }

        private string GetSwapContract(string currency)
        {
            if (currency == "ETH" || Currencies.IsEthereumToken(currency))
                return Account.Currencies.Get<EthereumConfig>(currency).SwapContractAddress;

            if (currency == "XTZ" || Currencies.IsTezosToken(currency))
                return Account.Currencies.Get<TezosConfig>(currency).SwapContractAddress;

            return null;
        }

        private static string GenerateOrderClientId() => Guid.NewGuid().ToByteArray().ToHexString(0, 16);
    }
}