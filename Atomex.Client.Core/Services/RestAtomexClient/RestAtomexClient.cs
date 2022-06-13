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
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Serilog;
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
        protected ISymbolsProvider SymbolsProvider { get; }
        protected ILogger Logger { get; }
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
            ISymbolsProvider symbolsProvider,
            ILogger logger
        )
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
            HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
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
                Logger.Information("{AtomexClientName} is starting for the {UserId} user [{Network}]", nameof(RestAtomexClient), AccountUserId, Account.Network);

                MarketDataRepository.Initialize(SymbolsProvider.GetSymbols(Account.Network));
                Logger.Debug("MarketDataRepository is initialized");

                await AuthenticateAsync(_cts.Token)
                    .ConfigureAwait(false);

                _isConnected = true;

                if (ServiceConnected != null)
                {
                    Logger.Debug("Fire the {EventName} event", nameof(ServiceConnected));
                    ServiceConnected.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));
                    ServiceConnected.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
                }

                await CancelAllOrdersAsync(_cts.Token)
                    .ConfigureAwait(false);

                _ = TrackSwapsAsync(_cts.Token);
                _ = RunAutoAuthorizationAsync(_cts.Token);

                Logger.Information("{AtomexClientName} has been started for the {UserId} user [{Network}]", nameof(RestAtomexClient), AccountUserId, Account.Network);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "An exception has been occurred when the {AtomexClientName} client is started for the {UserId} user [{Network}]",
                    nameof(RestAtomexClient), AccountUserId, Account.Network);

                await StopAsync();
            }
        }

        public Task StopAsync()
        {
            try
            {
                Logger.Information("{AtomexClientName} is stopping for the {UserId} user [{Network}]", nameof(RestAtomexClient), AccountUserId, Account.Network);

                _cts.Cancel();

                _isConnected = false;
                if (ServiceDisconnected != null)
                {
                    Logger.Debug("Fire the {EventName} event", nameof(ServiceDisconnected));
                    ServiceDisconnected.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
                    ServiceDisconnected.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));
                }

                MarketDataRepository.Clear();
                Logger.Debug("MarketDataRepository has been cleared");

                Logger.Information("{AtomexClientName} has been stopped for the {UserId} user [{Network}] ", nameof(RestAtomexClient), AccountUserId, Account.Network);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "An exception has been occurred when the {AtomexClientName} client is stopped for the {UserId} user [{Network}]",
                    nameof(RestAtomexClient), AccountUserId, Account.Network);
            }

            return Task.CompletedTask;
        }

        public MarketDataOrderBook GetOrderBook(string symbol) => MarketDataRepository.OrderBookBySymbol(symbol);

        public MarketDataOrderBook GetOrderBook(Symbol symbol) => MarketDataRepository.OrderBookBySymbol(symbol.Name);

        public Quote GetQuote(Symbol symbol) => MarketDataRepository.QuoteBySymbol(symbol.Name);

        public async Task CancelAllOrdersAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.Information("Canceling orders of the {UserId} user", AccountUserId);

                using var response = await HttpClient
                    .DeleteAsync("orders", _cts.Token)
                    .ConfigureAwait(false);
                var responseContent = await response.Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                var responseResult = JsonConvert.DeserializeObject<OrdersCancelatonDto>(responseContent, JsonSerializerSettings);

                Logger.Debug("{Count} orders of the {UserId} user are canceled", responseResult?.Count ?? 0, AccountUserId);

                var localDeletingResult = await Account.RemoveAllOrdersAsync()
                    .ConfigureAwait(false);

                if (!localDeletingResult)
                    Logger.Warning("The local \"Orders\" collection is not cleared");
                else
                    Logger.Debug("The local \"Orders\" collection is cleared");
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("The {TaskName} task has been canceled", nameof(CancelAllOrdersAsync));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Orders cancelation is failed for the {UserId} user", AccountUserId);
                throw;
            }
        }

        public async void OrderCancelAsync(long orderId, string symbol, Side side)
        {
            try
            {
                Logger.Information("Canceling an order: {OrderId}, \"{Symbol}\", {Side}. User is {UserId}",
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
                    Logger.Error("Order [{OrderId}, \"{Symbol}\", {Side}] cancelation is failed for the {UserId} user. " +
                        "Response: {ResponseMessage} [{ResponseStatusCode}]", orderId, symbol, side, AccountUserId, responseContent, response.StatusCode);

                    return;
                }

                var dbOrder = Account.GetOrderById(orderId);
                if (dbOrder == null)
                {
                    Logger.Warning("Canceled order [{OrderId}, \"{Symbol}\", {Side}] not found in the local database", orderId, symbol, side);

                    return;
                }

                dbOrder.Status = OrderStatus.Canceled;

                await Account.UpsertOrderAsync(dbOrder)
                    .ConfigureAwait(false);

                OrderReceived?.Invoke(this, new OrderEventArgs(dbOrder));

                Logger.Information("Order [{OrderId}, \"{Symbol}\", {Side}] is canceled. User is {UserId}",
                    orderId, symbol, side, AccountUserId);
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("The {TaskName} task has been canceled", nameof(OrderCancelAsync));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Order [{OrderId}, \"{Symbol}\", {Side}] cancelation is failed for the {UserId} user",
                    orderId, symbol, side, AccountUserId);
            }
        }

        public async void OrderSendAsync(Order order)
        {
            try
            {
                order.ClientOrderId = GenerateOrderClientId();

                Logger.Information("Sending the {OrderId} [{OrderClientId}] order...", order.Id, order.ClientOrderId);

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

                Logger.Debug("Sending the New Order DTO...: {@NewOrderDto}", newOrderDto);

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
                    Logger.Error("Sending the order is failed for the {UserId} user. New Order DTO: {@NewOrderDto}. " +
                        "Response: {ResponseMessage} [{ResponseStatusCode}]", AccountUserId, newOrderDto, responseContent, response.StatusCode);

                    return;
                }

                order.Id = JsonConvert.DeserializeObject<NewOrderResponseDto>(responseContent)?.OrderId ?? 0L;
                if (order.Id == 0)
                {
                    Logger.Warning("Response of the sent order has an invalid order id. It's not possible to add the order to the local DB. " +
                        "New Order DTO: {@NewOrderDto}. Response: {ResponseMessage} [{ResponseStatusCode}]", newOrderDto, responseContent, response.StatusCode);

                    return;
                }

                order.Status = OrderStatus.Placed;
                await Account.UpsertOrderAsync(order)
                    .ConfigureAwait(false);

                OrderReceived?.Invoke(this, new OrderEventArgs(order));
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("The {TaskName} task has been canceled", nameof(OrderSendAsync));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Sending the {OrderId} [{OrderClientId}] order is failed for the {UserId} user", AccountUserId, order.Id, order.ClientOrderId);
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
                Logger.Information("Initiating a swap...: {SwapId}, {SecretHash}, \"{Symbol}\", {ToAddress}, {RewardForRedeem}, {RefundAddress}",
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

                Logger.Debug("Sending the Initiate Swap DTO...: {@InitiateSwapDto}", initiateSwapDto);

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
                    Logger.Error("Sending the swap requisites is failed for the {UserId} user. Initiate Swap DTO: {@InitiateSwapDto}" +
                        "Response: {ResponseMessage} [{ResponseStatusCode}]", AccountUserId, initiateSwapDto, responseContent, response.StatusCode);

                    return;
                }

                await RequestActualSwapState(swapId, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("The {taskName} task has been canceled", nameof(SwapInitiateAsync));
            }
            catch (Exception ex)
            {

                Logger.Error(ex, "Swap initiation failed");
            }
        }

        public void SwapStatusAsync(string requestId, long swapId)
        {
            Logger.Debug("Requesting the actual status of the {SwapId} swap [{RequestId}]", swapId, requestId);
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

            Logger.Debug("Signing an authentication message using the \"{SigningAlgorithm}\" algorithm", signingAlgorithm);
            var signature = await Account.Wallet
                .SignByServiceKeyAsync(signingMessagePayload, AuthenticationAccountIndex, cancellationToken)
                .ConfigureAwait(false);
            Logger.Debug("The authentication message has been signed using the \"{SigningAlgorithm}\" algorithm", signingAlgorithm);

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
                Logger.Error("Authentication is failed for the {UserId} user. Response: {ResponseMessage} [{ResponseStatusCode}]", AccountUserId, responseContent, response.StatusCode);

                throw GetAuthenticationFailedException();
            }

            var authenticationData = JsonConvert.DeserializeObject<AuthenticationResponseData>(responseContent, JsonSerializerSettings);
            if (authenticationData == null)
            {
                Logger.Error("Authentication is failed for the {UserId} user. It's not possible to parse authentication data", AccountUserId);

                throw GetAuthenticationFailedException();
            }
            if (string.IsNullOrWhiteSpace(authenticationData.Token))
            {
                Logger.Error("Authentication is failed for the {UserId} user. Authentication token is invalid", AccountUserId);

                throw GetAuthenticationFailedException();
            }

            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authenticationData.Token}");
            _authenticationData = authenticationData;

            if (ServiceAuthenticated != null)
            {
                ServiceAuthenticated.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));
                ServiceAuthenticated.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
            }

            Logger.Information("The {UserId} user is authenticated until {AuthTokenExpiredDate}",
                AccountUserId, DateTimeOffset.FromUnixTimeMilliseconds(authenticationData.Expires).UtcDateTime);
        }

        protected Task TrackSwapsAsync(CancellationToken cancellationToken = default) => Task.Run(
            async () =>
            {
                try
                {
                    Logger.Information("Start to track swaps");

                    var localSwaps = await Account.GetSwapsAsync()
                        .ConfigureAwait(false);

                    var localSwapsCount = localSwaps.Count();
                    var lastSwapId = localSwapsCount > 0
                        ? localSwaps.MaxBy(s => s.Id).Id
                        : 0L;

                    Logger.Debug("Number of local swaps is {Count}. The last swap Id is {SwapId}", localSwapsCount, lastSwapId);

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
                    Logger.Debug("The {TaskName} task has been canceled", nameof(TrackSwapsAsync));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Swaps tracking failed");
                }
            },
            cancellationToken
        );

        protected Task RequestActualSwapState(long swapId, CancellationToken cancellationToken = default) => Task.Run(
            async () =>
            {
                try
                {
                    Logger.Debug("Requesting the actual state of the {SwapId} swap", swapId);

                    var (swap, needToWait) = await FetchUserSwapAsync(swapId, cancellationToken)
                        .ConfigureAwait(false);

                    if (swap == null)
                    {
                        Logger.Error("It's not possible to fetch the {SwapId} swap", swapId);

                        return;
                    }

                    Logger.Debug("The {SwapId} swap has been received. Apply its' state locally (handle this swap again)", swapId);
                    await HandleSwapAsync(swap, needToWait, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Logger.Debug("The {TaskName} task has been canceled", nameof(RequestActualSwapState));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Requesting the actual state of the {SwapId} swap failed", swapId);
                }
            },
            cancellationToken
        );

        protected async Task<(bool isSuccess, List<(Swap swap, bool needToWait)>? result)> FetchUserSwapsAsync(
            long lastSwapId = default,
            CancellationToken cancellationToken = default
        )
        {
            Logger.Debug("Fetching swaps of the {UserId} user. The start swap id is {SwapId}", AccountUserId, lastSwapId);

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
                Logger.Error("Failed to fetch user swaps. Response: {ResponseMessage} [{ResponseStatusCode}]", responseContent, response.StatusCode);

                return (false, null);
            }

            var swaps = new List<(Swap, bool)>(swapDtos.Count);
            foreach (var swapDto in swapDtos)
                swaps.Add((MapSwapDtoToSwap(swapDto), IsNeedToWaitSwap(swapDto)));

            return (true, swaps);
        }

        protected async Task<(Swap? swap, bool needToWait)> FetchUserSwapAsync(long swapId, CancellationToken cancellationToken = default)
        {
            Logger.Debug("Fetching the {SwapId} swap of the {UserId} user", AccountUserId, swapId);

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
                Logger.Error("Failed to fetch user swaps. Response: {ResponseMessage} [{ResponseStatusCode}]", responseContent, response.StatusCode);

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
                        Logger.Debug("Waiting for the authentication token to expire. Wait {Delay}", delay);

                        await Task.Delay(delay, cancellationToken)
                            .ConfigureAwait(false);

                        Logger.Debug("The authentication token will expire soon. Making a new request of authentication");
                        await AuthenticateAsync(cancellationToken)
                            .ConfigureAwait(false);
                        delay = GetDelay(_authenticationData!.Expires);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    Logger.Information("Auto authorization has been canceled");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Auto authorization failed");
                }
            },
            cancellationToken
        );

        protected virtual string GenerateClientOrderId() => Guid.NewGuid().ToByteArray().ToHexString(0, 16);

        private void ClearAuthenticationData()
        {
            var currentAuthorizationHeaderExists = HttpClient.DefaultRequestHeaders.Contains("Authorization");
            Logger.Debug("Clearing authentication data. The current authorization header exists == {@exists}", currentAuthorizationHeaderExists);

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
                    Logger.Debug("Wait {WaitingInterval} for the swap: {@Swap}", DefaultWaitingSwapInterval, swap);
                    await Task.Delay(DefaultWaitingSwapInterval, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    Logger.Debug("Handle the swap without waiting: {@Swap}", swap);
                }

                SwapReceived?.Invoke(this, new SwapEventArgs(swap));
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("The {TaskName} task has been canceled", nameof(HandleSwapAsync));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Handling the swap failed: {@Swap}", swap);
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
