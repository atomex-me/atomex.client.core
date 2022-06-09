using Atomex.Abstract;
using Atomex.Api.Rest;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography.Abstract;
using Atomex.EthereumTokens;
using Atomex.MarketData;
using Atomex.MarketData.Abstract;
using Atomex.Services.Abstract;
using Atomex.Swaps;
using Atomex.Swaps.Abstract;
using Atomex.TezosTokens;
using Atomex.Wallet.Abstract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Atomex.Services
{
    public partial class RestAtomexClient : IAtomexClient
    {
        private const string DEFAULT_AUTHENTICATION_MESSAGE = "Signing in ";
        private const uint DEFAULT_AUTHENTICATION_ACCOUNT_INDEX = 0u;
        private const uint DEFAULT_MAX_FETCHING_SWAPS_TRY_COUNT = 3u;
        private static readonly TimeSpan DefaultWaitingSwapInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan DefaultFetchingSwapsInterval = TimeSpan.FromSeconds(10);

        public event EventHandler<AtomexClientServiceEventArgs>? ServiceConnected;
        public event EventHandler<AtomexClientServiceEventArgs>? ServiceDisconnected;
        public event EventHandler<AtomexClientServiceEventArgs>? ServiceAuthenticated;
        public event EventHandler<AtomexClientErrorEventArgs>? Error;
        public event EventHandler<OrderEventArgs>? OrderReceived;
        public event EventHandler<SwapEventArgs>? SwapReceived;
        public event EventHandler<MarketDataEventArgs>? QuotesUpdated;

        public IAccount Account { get; }
        public IMarketDataRepository MarketDataRepository { get; }

        protected HttpClient HttpClient { get; }
        protected IConfiguration Configuration { get; }
        protected ISymbolsProvider SymbolsProvider { get; }
        protected ILogger<RestAtomexClient> Logger { get; }
        protected string AuthenticationMessage { get; } = DEFAULT_AUTHENTICATION_MESSAGE;
        protected uint AuthenticationAccountIndex { get; } = DEFAULT_AUTHENTICATION_ACCOUNT_INDEX;
        protected string AccountUserId => _accountUserIdLazy.Value;
        private JsonSerializerSettings JsonSerializerSettings { get; } = new()
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy { OverrideSpecifiedNames = false } }
        };

        private readonly Lazy<string> _accountUserIdLazy;
        private readonly CancellationTokenSource _cts = new();
        private bool _isConnected = false;
        private AuthenticationResponseData? _authenticationData;

        public RestAtomexClient(
            IAccount account,
            HttpClient httpClient,
            IConfiguration configuration,
            ISymbolsProvider symbolsProvider,
            ILogger<RestAtomexClient> logger
        )
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
            HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            SymbolsProvider = symbolsProvider ?? throw new ArgumentNullException(nameof(symbolsProvider));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));

            MarketDataRepository = new MarketDataRepository();
            _accountUserIdLazy = new Lazy<string>(() => Account.GetUserId(AuthenticationAccountIndex));
        }

        public bool IsAuthenticated => _authenticationData != null;

        public bool IsServiceConnected(AtomexClientService service) => Enum.IsDefined(typeof(AtomexClientService), service)
            ? _isConnected
            : throw new ArgumentOutOfRangeException(nameof(service), service, null);

        public async Task StartAsync()
        {
            try
            {
                Logger.LogInformation("{atomexClientName} is starting for the {userId} user [{network}]", nameof(RestAtomexClient), AccountUserId, Account.Network);

                MarketDataRepository.Initialize(SymbolsProvider.GetSymbols(Account.Network));
                Logger.LogDebug("MarketDataRepository is initialized");

                await AuthenticateAsync(_cts.Token)
                    .ConfigureAwait(false);

                _isConnected = true;

                if (ServiceConnected != null)
                {
                    Logger.LogDebug($"Fire the {nameof(ServiceConnected)} event");
                    ServiceConnected.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));
                    ServiceConnected.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
                }

                _ = CancelAllUserOrdersAsync(_cts.Token);
                _ = TrackSwapsAsync(_cts.Token);
                _ = RunAutoAuthorizationAsync(_cts.Token);

                Logger.LogInformation("{atomexClientName} has been started for the {userId} user [{network}]", nameof(RestAtomexClient), AccountUserId, Account.Network);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An exception has been occurred when the {atomexClientName} client is started for the {userId} user [{network}]",
                    nameof(RestAtomexClient), AccountUserId, Account.Network);
            }
        }

        public Task StopAsync()
        {
            try
            {
                Logger.LogInformation("{atomexClientName} is stopping for the {userId} user [{network}]", nameof(RestAtomexClient), AccountUserId, Account.Network);

                _cts.Cancel();

                _isConnected = false;
                if (ServiceDisconnected != null)
                {
                    Logger.LogDebug($"Fire the {nameof(ServiceDisconnected)} event");
                    ServiceDisconnected.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
                    ServiceDisconnected.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));
                }

                MarketDataRepository.Clear();
                Logger.LogDebug("MarketDataRepository has been cleared");

                Logger.LogInformation("{atomexClientName} has been stopped for the {userId} user [{network}] ", nameof(RestAtomexClient), AccountUserId, Account.Network);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An exception has been occurred when the {atomexClientName} client is stopped for the {userId} user [{network}]",
                    nameof(RestAtomexClient), AccountUserId, Account.Network);
            }

            return Task.CompletedTask;
        }

        public MarketDataOrderBook GetOrderBook(string symbol) => MarketDataRepository.OrderBookBySymbol(symbol);

        public MarketDataOrderBook GetOrderBook(Symbol symbol) => MarketDataRepository.OrderBookBySymbol(symbol.Name);

        public Quote GetQuote(Symbol symbol) => MarketDataRepository.QuoteBySymbol(symbol.Name);

        public Task CancelAllUserOrdersAsync(CancellationToken cancellationToken = default) => Task.Run(
            async () =>
            {
                try
                {
                    Logger.LogInformation("Canceling orders of the {userId} user", AccountUserId);

                    var queryParameters = await ConvertQueryParamsToStringAsync(new Dictionary<string, string>(2)
                    {
                        ["limit"] = "1000",
                        ["active"] = "true",
                    });

                    // TODO: use the CancelAllOrders API method
                    using var response = await HttpClient
                        .GetAsync($"orders?{queryParameters}", _cts.Token)
                        .ConfigureAwait(false);
                    var responseContent = await response.Content
                        .ReadAsStringAsync()
                        .ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.LogError("Failed to fetch active orders of the {userId} user. " +
                            "Response: {responseMessage} [{responseStatusCode}].", AccountUserId, responseContent, response.StatusCode);

                        return;
                    }

                    var orderDtos = JsonConvert.DeserializeObject<IEnumerable<OrderDto>?>(responseContent, JsonSerializerSettings);
                    var activeOrdersCount = orderDtos?.Count() ?? 0;

                    if (activeOrdersCount == 0)
                    {
                        Logger.LogInformation("The {userId} user doesn't have active orders. Cancel nothing", AccountUserId);

                        return;
                    }

                    Logger.LogInformation("The {userId} user has {count} active orders. Canceling...", AccountUserId, activeOrdersCount);

                    foreach (var order in orderDtos!)
                        OrderCancelAsync(order.Id, order.Symbol, order.Side);
                }
                catch (OperationCanceledException)
                {
                    Logger.LogDebug("The {taskName} task has been canceled", nameof(CancelAllUserOrdersAsync));
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Orders cancelation is failed for the {userId} user", AccountUserId);
                }
            },
            cancellationToken
        );

        public async void OrderCancelAsync(long orderId, string symbol, Side side)
        {
            try
            {
                Logger.LogInformation("Canceling an order: {orderId}, \"{symbol}\", {side}. User is {userId}",
                    orderId, symbol, side, AccountUserId);

                var queryParameters = await ConvertQueryParamsToStringAsync(new Dictionary<string, string>(2)
                {
                    ["symbol"] = symbol,
                    ["side"] = side.ToString(),
                });

                using var response = await HttpClient
                    .DeleteAsync($"orders/{orderId}?{queryParameters}", _cts.Token)
                    .ConfigureAwait(false);
                var responseContent = await response.Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                var responseResult = response.IsSuccessStatusCode
                    ? JsonConvert.DeserializeObject<OrderCancelationDto>(responseContent, JsonSerializerSettings)
                    : null;

                if (responseResult?.Result != true)
                {
                    Logger.LogError("Order [{orderId}, \"{symbol}\", {side}] cancelation is failed for the {userId} user. " +
                        "Response: {responseMessage} [{responseStatusCode}].", orderId, symbol, side, AccountUserId, responseContent, response.StatusCode);

                    return;
                }

                var dbOrder = Account.GetOrderById(orderId);
                if (dbOrder == null)
                {
                    Logger.LogWarning("Canceled order [{orderId}, \"{symbol}\", {side}] not found in the local database", orderId, symbol, side);

                    return;
                }

                dbOrder.Status = OrderStatus.Canceled;

                await Account.UpsertOrderAsync(dbOrder)
                    .ConfigureAwait(false);

                OrderReceived?.Invoke(this, new OrderEventArgs(dbOrder));

                Logger.LogInformation("Order [{orderId}, \"{symbol}\", {side}] is canceled. User is {userId}",
                    orderId, symbol, side, AccountUserId);
            }
            catch (OperationCanceledException)
            {
                Logger.LogDebug("The {taskName} task has been canceled", nameof(OrderCancelAsync));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Order [{orderId}, \"{symbol}\", {side}] cancelation is failed for the {userId} user",
                    orderId, symbol, side, AccountUserId);
            }
        }

        public async void OrderSendAsync(Order order)
        {
            try
            {
                order.ClientOrderId = GenerateOrderClientId();

                Logger.LogInformation("Sending the order...: {@order}", order);

                await Account.UpsertOrderAsync(order)
                    .ConfigureAwait(false);

                var baseCurrencyContract = GetSwapContract(order.Symbol.BaseCurrency());
                var quoteCurrencyContract = GetSwapContract(order.Symbol.QuoteCurrency());
                // TODO: we can only use a proof of possession when a signing algorithm can be received
                //await order.CreateProofOfPossessionAsync(Account)
                //    .ConfigureAwait(false);

                var newOrderDto = new NewOrderDto(
                    ClientOrderId: order.ClientOrderId,
                    Symbol: order.Symbol,
                    Price: order.Price,
                    Qty: order.Qty,
                    Side: order.Side,
                    Type: order.Type,
                    Requisites: new(
                        BaseCurrencyContract: baseCurrencyContract,
                        QuoteCurrencyContract: quoteCurrencyContract
                    )
                )
                {
                    // TODO: we can only use a proof of possession when a signing algorithm can be received
                    //ProofsOfFunds = order.FromWallets.Select(walletAddress => new ProofOfFundsDto(
                    //    Address: walletAddress.Address,
                    //    Currency: walletAddress.Currency,
                    //    TimeStamp: order.TimeStamp,
                    //    Message: walletAddress.Nonce,
                    //    PublicKey: walletAddress.PublicKey,
                    //    Signature: walletAddress.ProofOfPossession,
                    //    Algorithm: string.Empty // ?
                    //)),
                };

                var response = await HttpClient.PostAsync(
                    "orders",
                    new StringContent(
                        content: JsonConvert.SerializeObject(newOrderDto),
                        encoding: Encoding.UTF8,
                        mediaType: "application/json"
                    ),
                    _cts.Token
                ).ConfigureAwait(false);

                var responseContent = await response.Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError("Sending the order is failed for the {userId} user. New Order DTO: {@newOrderDto}. " +
                        "Response: {responseMessage} [{responseStatusCode}].", AccountUserId, newOrderDto, responseContent, response.StatusCode);

                    return;
                }

                order.Id = JsonConvert.DeserializeObject<NewOrderResponseDto>(responseContent)?.OrderId ?? 0L;
                if (order.Id == 0)
                {
                    Logger.LogWarning("Response of the sent order has an invalid order id. It's not possible to add the order to the local DB. " +
                        "New Order DTO: {@newOrderDto}. Response: {responseMessage} [{responseStatusCode}].", newOrderDto, responseContent, response.StatusCode);

                    return;
                }

                order.Status = OrderStatus.Placed;
                await Account.UpsertOrderAsync(order)
                    .ConfigureAwait(false);

                OrderReceived?.Invoke(this, new OrderEventArgs(order));
            }
            catch (OperationCanceledException)
            {
                Logger.LogDebug("The {taskName} task has been canceled", nameof(OrderSendAsync));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Sending the order is failed for the {userId} user. Order: {@order}", AccountUserId, order);
            }
        }

        public void SubscribeToMarketData(SubscriptionType type)
        {
            // nothing to do...
        }

        public void SwapAcceptAsync(long id, string symbol, string toAddress, decimal rewardForRedeem, string refundAddress)
        {
            throw new NotImplementedException();
        }

        public async void SwapInitiateAsync(long swapId, byte[] secretHash, string symbol, string toAddress, decimal rewardForRedeem, string refundAddress)
        {
            try
            {
                Logger.LogInformation("Initiating a swap...: {swapId}, {secretHash}, {symbol}, {toAddress}, {rewardForRedeem}, {refundAddress}",
                    swapId, secretHash, symbol, toAddress, rewardForRedeem, refundAddress);

                var initiateSwapDto = new InitiateSwapDto(
                    ReceivingAddress: toAddress,
                    RewardForRedeem: rewardForRedeem,
                    LockTime: CurrencySwap.DefaultInitiatorLockTimeInSeconds
                )
                {
                    RefundAddress = refundAddress,
                    SecretHash = secretHash.ToHexString(),
                };

                var response = await HttpClient.PostAsync(
                    $"swaps/{swapId}/requisites",
                    new StringContent(
                        content: JsonConvert.SerializeObject(initiateSwapDto),
                        encoding: Encoding.UTF8,
                        mediaType: "application/json"
                    ),
                    _cts.Token
                ).ConfigureAwait(false);

                var responseContent = await response.Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                var result = response.IsSuccessStatusCode
                    ? JsonConvert.DeserializeObject<InitiateSwapResponseDto>(responseContent, JsonSerializerSettings)
                    : null;

                if (result?.Result != true)
                {
                    Logger.LogError("Sending the swap requisites is failed for the {userId} user. Swap Initiation DTO: {@initiateSwapDto}" +
                        "Response: {responseMessage} [{responseStatusCode}]", AccountUserId, initiateSwapDto, responseContent, response.StatusCode);

                    return;
                }

                await RequestActualSwapState(swapId, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger.LogDebug("The {taskName} task has been canceled", nameof(SwapInitiateAsync));
            }
            catch (Exception ex)
            {

                Logger.LogError(ex, "Swap initiation failed");
            }
        }

        public void SwapStatusAsync(string requestId, long swapId)
        {
            Logger.LogDebug("Requesting the actual status of the {swapId} swap [{requestId}]", swapId, requestId);
            _ = RequestActualSwapState(swapId, _cts.Token);
        }

        protected async Task AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            const string signingAlgorithm = "Sha256WithEcdsa:BtcMsg";
            using var securePublicKey = Account.Wallet.GetServicePublicKey(AuthenticationAccountIndex);
            var publicKey = securePublicKey.ToUnsecuredBytes();

            var timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var signingMessage = AuthenticationMessage + timeStamp.ToString();
            var signingMessagePayload = HashAlgorithm.Sha256.Hash(
                BitcoinUtils.FormatMessageForSigning(Encoding.UTF8.GetBytes(signingMessage)),
                iterations: 2
            );

            Logger.LogDebug("Signing an authentication message using the \"{signingAlgorithm}\" algorithm", signingAlgorithm);
            var signature = await Account.Wallet
                .SignByServiceKeyAsync(signingMessagePayload, AuthenticationAccountIndex, cancellationToken)
                .ConfigureAwait(false);
            Logger.LogDebug("The authentication message has been signed using the \"{signingAlgorithm}\" algorithm", signingAlgorithm);

            var authenticationRequestContent = new AuthenticationRequestData(
                Message: AuthenticationMessage,
                TimeStamp: timeStamp,
                PublicKey: Hex.ToHexString(publicKey),
                Signature: Hex.ToHexString(signature),
                Algorithm: signingAlgorithm
            );

            ClearAuthenticationData();

            using var response = await HttpClient
                .PostAsync(
                    "token",
                    new StringContent(
                        content: JsonConvert.SerializeObject(authenticationRequestContent, JsonSerializerSettings),
                        encoding: Encoding.UTF8,
                        mediaType: "application/json"
                    ),
                    cancellationToken
                ).ConfigureAwait(false);

            var responseContent = await response.Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogError("Authentication is failed for the {userId} user. Response: {responseMessage} [{responseStatusCode}]", AccountUserId, responseContent, response.StatusCode);

                throw GetAuthenticationFailedException();
            }

            var authenticationData = JsonConvert.DeserializeObject<AuthenticationResponseData>(responseContent, JsonSerializerSettings);
            if (authenticationData == null)
            {
                Logger.LogError("Authentication is failed for the {userId} user. It's not possible to parse authentication data", AccountUserId);

                throw GetAuthenticationFailedException();
            }
            if (string.IsNullOrWhiteSpace(authenticationData.Token))
            {
                Logger.LogError("Authentication is failed for the {userId} user. Authentication token is invalid", AccountUserId);

                throw GetAuthenticationFailedException();
            }

            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authenticationData.Token}");
            _authenticationData = authenticationData;

            if (ServiceAuthenticated != null)
            {
                ServiceAuthenticated.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));
                ServiceAuthenticated.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
            }

            Logger.LogInformation("The {userId} user is authenticated until {authTokenExpiredDate}",
                AccountUserId, DateTimeOffset.FromUnixTimeMilliseconds(authenticationData.Expires).UtcDateTime);
        }

        protected Task TrackSwapsAsync(CancellationToken cancellationToken = default) => Task.Run(
            async () =>
            {
                try
                {
                    Logger.LogInformation("Start to track swaps");

                    var localSwaps = await Account.GetSwapsAsync()
                        .ConfigureAwait(false);

                    var localSwapsCount = localSwaps.Count();
                    var lastSwapId = localSwapsCount > 0
                        ? localSwaps.MaxBy(s => s.Id).Id
                        : 0L;

                    Logger.LogDebug("Number of local swaps is {count}. The last swap Id is {swapId}", localSwapsCount, lastSwapId);

                    var tryCount = 0u;

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var (isSuccess, swapInfos) = await FetchUserSwapsAsync(lastSwapId, cancellationToken)
                            .ConfigureAwait(false);

                        if (isSuccess && swapInfos != null)
                        {
                            tryCount = 0u;
                            foreach (var (swap, needToWait) in swapInfos)
                            {
                                lastSwapId = Math.Max(lastSwapId, swap.Id);
                                _ = HandleSwapAsync(swap, needToWait, cancellationToken);
                            }
                        }
                        else
                        {
                            if (++tryCount >= DEFAULT_MAX_FETCHING_SWAPS_TRY_COUNT)
                                throw new Exception("It's not possible to fetch user swaps");
                        }

                        await Task.Delay(DefaultFetchingSwapsInterval, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.LogDebug("The {taskName} task has been canceled", nameof(TrackSwapsAsync));
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Swaps tracking failed");
                }
            },
            cancellationToken
        );

        protected Task RequestActualSwapState(long swapId, CancellationToken cancellationToken = default) => Task.Run(
            async () =>
            {
                try
                {
                    Logger.LogDebug("Requesting the actual state of the {swapId} swap", swapId);

                    var (swap, needToWait) = await FetchUserSwapAsync(swapId, cancellationToken)
                        .ConfigureAwait(false);

                    if (swap == null)
                    {
                        Logger.LogError("It's not possible to fetch the {swapId} swap", swapId);

                        return;
                    }

                    Logger.LogDebug("The {swapId} swap has been received. Apply its' state locally (handle this swap again)", swapId);
                    await HandleSwapAsync(swap, needToWait, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Logger.LogDebug("The {taskName} task has been canceled", nameof(RequestActualSwapState));
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Requesting the actual state of the {swapId} swap failed", swapId);
                }
            },
            cancellationToken
        );

        protected async Task<(bool isSuccess, List<(Swap swap, bool needToWait)>? result)> FetchUserSwapsAsync(
            long lastSwapId = default,
            CancellationToken cancellationToken = default
        )
        {
            Logger.LogDebug("Fetching swaps of the {userId} user. The start swap id is {swapId}", AccountUserId, lastSwapId);

            using var response = await HttpClient.GetAsync($"swaps?afterId={lastSwapId}", cancellationToken)
                .ConfigureAwait(false);
            var responseContent = await response.Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            var swapDtos = response.IsSuccessStatusCode
                ? JsonConvert.DeserializeObject<List<SwapDto>>(responseContent, JsonSerializerSettings)
                : null;

            if (swapDtos == null)
            {
                Logger.LogError("Failed to fetch user swaps. Response: {responseMessage} [{responseStatusCode}]", responseContent, response.StatusCode);

                return (false, null);
            }

            var swaps = new List<(Swap, bool)>(swapDtos.Count);
            foreach (var swapDto in swapDtos)
                swaps.Add((MapSwapDtoToSwap(swapDto), IsNeedToWaitSwap(swapDto)));

            return (true, swaps);
        }

        protected async Task<(Swap? swap, bool needToWait)> FetchUserSwapAsync(long swapId, CancellationToken cancellationToken = default)
        {
            Logger.LogDebug("Fetching the {swapId} swap of the {userId} user", AccountUserId, swapId);

            using var response = await HttpClient.GetAsync($"swaps/{swapId}", cancellationToken)
                .ConfigureAwait(false);
            var responseContent = await response.Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            var swapDto = response.IsSuccessStatusCode
                ? JsonConvert.DeserializeObject<SwapDto>(responseContent, JsonSerializerSettings)
                : null;

            if (swapDto == null)
            {
                Logger.LogError("Failed to fetch user swaps. Response: {responseMessage} [{responseStatusCode}]", responseContent, response.StatusCode);

                return (null, false);
            }

            return (MapSwapDtoToSwap(swapDto), IsNeedToWaitSwap(swapDto));
        }

        protected Task RunAutoAuthorizationAsync(CancellationToken cancellationToken = default) => Task.Run(
            async () =>
            {
                static TimeSpan GetDelay(long authTokenExpiredTimeStamp)
                {
                    var delay = DateTimeOffset.FromUnixTimeMilliseconds(authTokenExpiredTimeStamp - 10L * 60L * 1000L) - DateTimeOffset.UtcNow;
                    return delay >= TimeSpan.Zero ? delay : TimeSpan.Zero;
                }

                var delay = GetDelay(_authenticationData != null ? _authenticationData.Expires : 0L);

                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        Logger.LogDebug("Waiting for the authentication token to expire. Wait {delay}", delay);

                        await Task.Delay(delay, cancellationToken)
                            .ConfigureAwait(false);

                        Logger.LogDebug("The authentication token will expire soon. Making a new request of authentication");
                        await AuthenticateAsync(cancellationToken)
                            .ConfigureAwait(false);
                        delay = GetDelay(_authenticationData!.Expires);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    Logger.LogInformation("Auto authorization has been canceled");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Auto authorization failed");
                }
            },
            cancellationToken
        );

        protected virtual string GenerateClientOrderId() => Guid.NewGuid().ToByteArray().ToHexString(0, 16);

        private void ClearAuthenticationData()
        {
            var currentAuthorizationHeaderExists = HttpClient.DefaultRequestHeaders.Contains("Authorization");
            Logger.LogDebug("Clearing authentication data. The current authorization header exists == {@exists}", currentAuthorizationHeaderExists);

            _authenticationData = null;
            if (currentAuthorizationHeaderExists)
                HttpClient.DefaultRequestHeaders.Remove("Authorization");
        }

        private string? GetSwapContract(string currency) => currency switch
        {
            "ETH" => Account.Currencies.Get<EthereumConfig>(currency).SwapContractAddress,
            "USDT" or "TBTC" or "WBTC" => Account.Currencies.Get<Erc20Config>(currency).SwapContractAddress,

            "XTZ" => Account.Currencies.Get<TezosConfig>(currency).SwapContractAddress,
            "FA12" or "TZBTC" or "KUSD" => Account.Currencies.Get<Fa12Config>(currency).SwapContractAddress,
            "FA2" => Account.Currencies.Get<Fa2Config>(currency).SwapContractAddress,
            _ => null
        };

        private Task HandleSwapAsync(
            Swap swap,
            bool neetToWait = false,
            CancellationToken cancellationToken = default
        ) => Task.Run(async () =>
        {
            try
            {
                if (neetToWait)
                {
                    Logger.LogDebug("Wait {waitInterval} for the swap: {@swap}", DefaultWaitingSwapInterval, swap);
                    await Task.Delay(DefaultWaitingSwapInterval, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    Logger.LogDebug("Handle the swap without waiting: {@swap}", swap);
                }

                SwapReceived?.Invoke(this, new SwapEventArgs(swap));
            }
            catch (OperationCanceledException)
            {
                Logger.LogDebug("The {taskName} task has been canceled", nameof(HandleSwapAsync));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Handling the {swapId} swap failed", swap.Id);
            }
        }, cancellationToken);

        private bool IsNeedToWaitSwap(SwapDto swapDto)
            => swapDto.User?.Status == PartyStatus.Created || swapDto.CounterParty?.Status == PartyStatus.Created;

        private Swap MapSwapDtoToSwap(SwapDto swapDto)
        {
            var swapStatus = SwapStatus.Empty;

            if (swapDto.User?.Status > PartyStatus.Created)
                swapStatus |= (swapDto.IsInitiator ? SwapStatus.Accepted : SwapStatus.Initiated);

            if (swapDto.CounterParty?.Status > PartyStatus.Created)
                swapStatus |= (swapDto.IsInitiator ? SwapStatus.Initiated : SwapStatus.Accepted);

            return new Swap()
            {
                Id = swapDto.Id,
                SecretHash = !string.IsNullOrWhiteSpace(swapDto.SecretHash)
                    ? Hex.FromString(swapDto.SecretHash)
                    : null,
                Status = swapStatus,

                TimeStamp = swapDto.TimeStamp,
                Symbol = swapDto.Symbol,
                Side = swapDto.Side,
                Price = swapDto.Price,
                Qty = swapDto.Qty,
                IsInitiative = swapDto.IsInitiator,

                ToAddress = swapDto.User?.Requisites?.ReceivingAddress,
                RewardForRedeem = swapDto.User?.Requisites?.RewardForRedeem ?? 0m,
                OrderId = swapDto.User?.Trades?.FirstOrDefault()?.OrderId ?? 0L,

                PartyAddress = swapDto.CounterParty?.Requisites?.ReceivingAddress,
                PartyRewardForRedeem = swapDto.CounterParty?.Requisites?.RewardForRedeem ?? 0m,
            };
        }

        private static string GenerateOrderClientId() => Guid.NewGuid().ToByteArray().ToHexString(0, 16);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static Exception GetAuthenticationFailedException() => new("Authentication is failed");

        private static async Task<string> ConvertQueryParamsToStringAsync(Dictionary<string, string> urlParams)
        {
            using var content = new FormUrlEncodedContent(urlParams);

            return await content.ReadAsStringAsync()
                .ConfigureAwait(false);
        }
    }
}
