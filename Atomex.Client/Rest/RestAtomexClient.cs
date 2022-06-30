using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using Atomex.Client.Abstract;
using Atomex.Client.Common;
using Atomex.Client.Entities;
using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;
using Atomex.Common;
using Atomex.Cryptography.Abstract;
using Atomex.MarketData.Common;

#nullable enable

namespace Atomex.Client.Rest
{
    public partial class RestAtomexClient : IAtomexClient
    {
        private const string DEFAULT_AUTHENTICATION_MESSAGE = "Signing in ";
        private const uint DEFAULT_AUTHENTICATION_ACCOUNT_INDEX = 0u;
        private const uint DEFAULT_MAX_FETCHING_SWAPS_TRY_COUNT = 3u;
        private static readonly TimeSpan DefaultWaitingSwapInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan DefaultFetchingSwapsInterval = TimeSpan.FromSeconds(10);

        public event EventHandler<ServiceEventArgs> ServiceStatusChanged;
        public event EventHandler<ServiceErrorEventArgs> Error;
        public event EventHandler<OrderEventArgs> OrderUpdated;
        public event EventHandler<SwapEventArgs> SwapUpdated;
        public event EventHandler<QuotesEventArgs> QuotesUpdated;
        public event EventHandler<EntriesEventArgs> EntriesUpdated;
        public event EventHandler<SnapshotEventArgs> SnapshotUpdated;

        protected HttpClient HttpClient { get; }
        protected ILogger _log;
        protected string AuthenticationMessage { get; } = DEFAULT_AUTHENTICATION_MESSAGE;
        protected uint AuthenticationAccountIndex { get; } = DEFAULT_AUTHENTICATION_ACCOUNT_INDEX;
        protected string AccountUserId { get; private set; }
        private JsonSerializerSettings JsonSerializerSettings { get; } = new()
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy { OverrideSpecifiedNames = false } }
        };

        private readonly CancellationTokenSource _cts = new();
        private bool _isConnected = false;
        private AuthenticationResponseData? _authenticationData;
        private readonly LastLocalSwapIdProvider _lastLocalSwapIdProvider;
        private readonly AuthMessageSigner _authMessageSigner;

        public RestAtomexClient(
            HttpClient httpClient,
            LastLocalSwapIdProvider lastLocalSwapIdProvider,
            AuthMessageSigner authMessageSigner,
            ILogger logger = null
        )
        {
            HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _lastLocalSwapIdProvider = lastLocalSwapIdProvider ?? throw new ArgumentNullException(nameof(lastLocalSwapIdProvider));
            _authMessageSigner = authMessageSigner ?? throw new ArgumentNullException(nameof(authMessageSigner));
            _log = logger;
        }

        public bool IsServiceConnected(Service service) => Enum.IsDefined(typeof(Service), service)
            ? _isConnected
            : throw new ArgumentOutOfRangeException(nameof(service), service, null);

        public async Task StartAsync()
        {
            try
            {
                _log?.LogInformation("{AtomexClientName} is starting for the {UserId} user", nameof(RestAtomexClient), AccountUserId);

                await AuthenticateAsync(_cts.Token)
                    .ConfigureAwait(false);

                _isConnected = true;

                if (ServiceStatusChanged != null)
                {
                    _log?.LogDebug("Fire the {EventName} event", nameof(ServiceStatusChanged));
                    ServiceStatusChanged.Invoke(this, new ServiceEventArgs(Service.Exchange, ServiceStatus.Connected));
                    ServiceStatusChanged.Invoke(this, new ServiceEventArgs(Service.MarketData, ServiceStatus.Connected));
                }

                await CancelAllOrdersAsync(_cts.Token)
                    .ConfigureAwait(false);

                _ = TrackSwapsAsync(_cts.Token);
                _ = RunAutoAuthorizationAsync(_cts.Token);

                _log?.LogInformation("{AtomexClientName} has been started for the {UserId} user",
                    nameof(RestAtomexClient), AccountUserId);
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "An exception has been occurred when the {AtomexClientName} client is started for the {UserId} user",
                    nameof(RestAtomexClient), AccountUserId);

                await StopAsync();
            }
        }

        public Task StopAsync()
        {
            try
            {
                _log?.LogInformation("{AtomexClientName} is stopping for the {UserId} user", nameof(RestAtomexClient), AccountUserId);

                _cts.Cancel();

                _isConnected = false;

                if (ServiceStatusChanged != null)
                {
                    _log?.LogDebug("Fire the {EventName} event", nameof(ServiceStatusChanged));
                    ServiceStatusChanged.Invoke(this, new ServiceEventArgs(Service.MarketData, ServiceStatus.Disconnected));
                    ServiceStatusChanged.Invoke(this, new ServiceEventArgs(Service.Exchange, ServiceStatus.Disconnected));
                }

                _log?.LogInformation("{AtomexClientName} has been stopped for the {UserId} user ",
                    nameof(RestAtomexClient), AccountUserId);
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "An exception has been occurred when the {AtomexClientName} client is stopped for the {UserId} user",
                    nameof(RestAtomexClient), AccountUserId);
            }

            return Task.CompletedTask;
        }

        public async Task CancelAllOrdersAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _log?.LogInformation("Canceling orders of the {UserId} user", AccountUserId);

                using var response = await HttpClient
                    .DeleteAsync("orders", _cts.Token)
                    .ConfigureAwait(false);

                var responseContent = await response.Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                var responseResult = JsonConvert.DeserializeObject<OrdersCancelatonDto>(responseContent, JsonSerializerSettings);

                _log?.LogDebug("{Count} orders of the {UserId} user are canceled", responseResult?.Count ?? 0, AccountUserId);
            }
            catch (OperationCanceledException)
            {
                _log?.LogDebug("The {TaskName} task has been canceled", nameof(CancelAllOrdersAsync));
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "Orders cancelation is failed for the {UserId} user", AccountUserId);
                throw;
            }
        }

        public async void OrderCancelAsync(long orderId, string symbol, Side side)
        {
            try
            {
                _log?.LogInformation("Canceling an order: {OrderId}, \"{Symbol}\", {Side}. User is {UserId}",
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
                    _log?.LogError("Order [{OrderId}, \"{Symbol}\", {Side}] cancelation is failed for the {UserId} user. " +
                        "Response: {ResponseMessage} [{ResponseStatusCode}]", orderId, symbol, side, AccountUserId, responseContent, response.StatusCode);

                    return;
                }

                _log?.LogInformation("Order [{OrderId}, \"{Symbol}\", {Side}] is canceled. User is {UserId}",
                    orderId, symbol, side, AccountUserId);

                var order = await FetchUserOrderAsync(orderId)
                    .ConfigureAwait(false);

                if (order != null)
                    OrderUpdated?.Invoke(this, new OrderEventArgs(order));
            }
            catch (OperationCanceledException)
            {
                _log?.LogDebug("The {TaskName} task has been canceled", nameof(OrderCancelAsync));
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "Order [{OrderId}, \"{Symbol}\", {Side}] cancelation is failed for the {UserId} user",
                    orderId, symbol, side, AccountUserId);
            }
        }

        public async void OrderSendAsync(Order order)
        {
            try
            {
                _log?.LogInformation("Sending the {OrderId} [{OrderClientId}] order...", order.Id, order.ClientOrderId);

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
                        BaseCurrencyContract: order.BaseCurrencyContract,
                        QuoteCurrencyContract: order.QuoteCurrencyContract
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

                _log?.LogDebug("Sending the New Order DTO...: {@NewOrderDto}", newOrderDto);

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
                    _log?.LogError("Sending the order is failed for the {UserId} user. New Order DTO: {@NewOrderDto}. " +
                        "Response: {ResponseMessage} [{ResponseStatusCode}]", AccountUserId, newOrderDto, responseContent, response.StatusCode);

                    return;
                }

                order.Id = JsonConvert.DeserializeObject<NewOrderResponseDto>(responseContent)?.OrderId ?? 0L;

                if (order.Id == 0)
                {
                    _log?.LogWarning("Response of the sent order has an invalid order id. It's not possible to add the order to the local DB. " +
                        "New Order DTO: {@NewOrderDto}. Response: {ResponseMessage} [{ResponseStatusCode}]", newOrderDto, responseContent, response.StatusCode);

                    return;
                }

                order.Status = OrderStatus.Placed;
 
                OrderUpdated?.Invoke(this, new OrderEventArgs(order));
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

        public void SubscribeToMarketData(SubscriptionType type)
        {
            // nothing to do...
        }

        public void SwapAcceptAsync(
            long id,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress,
            ulong lockTime)
        {
            throw new NotImplementedException();
        }

        public async void SwapInitiateAsync(
            long swapId,
            byte[] secretHash,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress,
            ulong lockTime)
        {
            try
            {
                _log?.LogInformation("Initiating a swap...: {SwapId}, {SecretHash}, \"{Symbol}\", {ToAddress}, {RewardForRedeem}, {RefundAddress}",
                    swapId, secretHash, symbol, toAddress, rewardForRedeem, refundAddress);

                var initiateSwapDto = new InitiateSwapDto(
                    ReceivingAddress: toAddress,
                    RewardForRedeem: rewardForRedeem,
                    LockTime: lockTime
                )
                {
                    RefundAddress = refundAddress,
                    SecretHash = secretHash.ToHexString(),
                };

                _log?.LogDebug("Sending the Initiate Swap DTO...: {@InitiateSwapDto}", initiateSwapDto);

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
                    _log?.LogError("Sending the swap requisites is failed for the {UserId} user. Initiate Swap DTO: {@InitiateSwapDto}" +
                        "Response: {ResponseMessage} [{ResponseStatusCode}]", AccountUserId, initiateSwapDto, responseContent, response.StatusCode);

                    return;
                }

                await RequestActualSwapState(swapId, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                _log?.LogDebug("The {taskName} task has been canceled", nameof(SwapInitiateAsync));
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "Swap initiation failed");
            }
        }

        public void SwapStatusAsync(string requestId, long swapId)
        {
            _log?.LogDebug("Requesting the actual status of the {SwapId} swap [{RequestId}]", swapId, requestId);
            _ = RequestActualSwapState(swapId, _cts.Token);
        }

        protected async Task AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            const string signingAlgorithm = "Sha256WithEcdsa:BtcMsg";

            var timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var signingMessage = AuthenticationMessage + timeStamp.ToString();
            var signingMessagePayload = HashAlgorithm.Sha256.Hash(
                BitcoinUtils.FormatMessageForSigning(Encoding.UTF8.GetBytes(signingMessage)),
                iterations: 2
            );

            _log?.LogDebug("Signing an authentication message using the \"{SigningAlgorithm}\" algorithm", signingAlgorithm);

            var (publicKey, signature) = await _authMessageSigner
                .Invoke(signingMessagePayload, signingAlgorithm)
                .ConfigureAwait(false);

            AccountUserId = HashAlgorithm.Sha256.Hash(publicKey, iterations: 2).ToHexString();

            _log?.LogDebug("The authentication message has been signed using the \"{SigningAlgorithm}\" algorithm", signingAlgorithm);

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
                _log?.LogError("Authentication is failed for the {UserId} user. Response: {ResponseMessage} [{ResponseStatusCode}]", AccountUserId, responseContent, response.StatusCode);

                throw GetAuthenticationFailedException();
            }

            var authenticationData = JsonConvert.DeserializeObject<AuthenticationResponseData>(responseContent, JsonSerializerSettings);
            if (authenticationData == null)
            {
                _log?.LogError("Authentication is failed for the {UserId} user. It's not possible to parse authentication data", AccountUserId);

                throw GetAuthenticationFailedException();
            }
            if (string.IsNullOrWhiteSpace(authenticationData.Token))
            {
                _log?.LogError("Authentication is failed for the {UserId} user. Authentication token is invalid", AccountUserId);

                throw GetAuthenticationFailedException();
            }

            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authenticationData.Token}");
            _authenticationData = authenticationData;

            if (ServiceStatusChanged != null)
            {
                ServiceStatusChanged.Invoke(this, new ServiceEventArgs(Service.Exchange, ServiceStatus.Authenticated));
                ServiceStatusChanged.Invoke(this, new ServiceEventArgs(Service.MarketData, ServiceStatus.Authenticated));
            }

            _log?.LogInformation("The {UserId} user is authenticated until {AuthTokenExpiredDate}",
                AccountUserId, DateTimeOffset.FromUnixTimeMilliseconds(authenticationData.Expires).UtcDateTime);
        }

        protected Task TrackSwapsAsync(CancellationToken cancellationToken = default) => Task.Run(
            async () =>
            {
                try
                {
                    _log?.LogInformation("Start to track swaps");

                    var lastSwapId = await _lastLocalSwapIdProvider
                        .Invoke()
                        .ConfigureAwait(false);

                    _log?.LogDebug("The last swap Id is {SwapId}", lastSwapId);

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
                    _log?.LogDebug("The {TaskName} task has been canceled", nameof(TrackSwapsAsync));
                }
                catch (Exception ex)
                {
                    _log?.LogError(ex, "Swaps tracking failed");
                }
            },
            cancellationToken
        );

        protected Task RequestActualSwapState(
            long swapId,
            CancellationToken cancellationToken = default) => Task.Run(
            async () =>
            {
                try
                {
                    _log?.LogDebug("Requesting the actual state of the {SwapId} swap", swapId);

                    var (swap, needToWait) = await FetchUserSwapAsync(swapId, cancellationToken)
                        .ConfigureAwait(false);

                    if (swap == null)
                    {
                        _log?.LogError("It's not possible to fetch the {SwapId} swap", swapId);

                        return;
                    }

                    _log?.LogDebug("The {SwapId} swap has been received. Apply its' state locally (handle this swap again)", swapId);

                    await HandleSwapAsync(swap, needToWait, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _log?.LogDebug("The {TaskName} task has been canceled", nameof(RequestActualSwapState));
                }
                catch (Exception ex)
                {
                    _log?.LogError(ex, "Requesting the actual state of the {SwapId} swap failed", swapId);
                }
            },
            cancellationToken
        );

        protected async Task<(bool isSuccess, List<(Swap swap, bool needToWait)>? result)> FetchUserSwapsAsync(
            long lastSwapId = default,
            CancellationToken cancellationToken = default
        )
        {
            _log?.LogDebug("Fetching swaps of the {UserId} user. The start swap id is {SwapId}", AccountUserId, lastSwapId);

            using var response = await HttpClient
                .GetAsync($"swaps?afterId={lastSwapId}", cancellationToken)
                .ConfigureAwait(false);

            var responseContent = await response.Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            var swapDtos = response.IsSuccessStatusCode
                ? JsonConvert.DeserializeObject<List<SwapDto>>(responseContent, JsonSerializerSettings)
                : null;

            if (swapDtos == null)
            {
                _log?.LogError("Failed to fetch user swaps. Response: {ResponseMessage} [{ResponseStatusCode}]", responseContent, response.StatusCode);

                return (false, null);
            }

            var swaps = new List<(Swap, bool)>(swapDtos.Count);

            foreach (var swapDto in swapDtos)
                swaps.Add((MapSwapDtoToSwap(swapDto), IsNeedToWaitSwap(swapDto)));

            return (true, swaps);
        }

        protected async Task<(Swap? swap, bool needToWait)> FetchUserSwapAsync(
            long swapId,
            CancellationToken cancellationToken = default)
        {
            _log?.LogDebug("Fetching the {SwapId} swap of the {UserId} user", swapId, AccountUserId);

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
                _log?.LogError("Failed to fetch user swaps. Response: {ResponseMessage} [{ResponseStatusCode}]", responseContent, response.StatusCode);

                return (null, false);
            }

            return (MapSwapDtoToSwap(swapDto), IsNeedToWaitSwap(swapDto));
        }

        protected async Task<Order?> FetchUserOrderAsync(
            long orderId,
            CancellationToken cancellationToken = default)
        {
            _log?.LogDebug("Fetching the {OrderId} order of the {UserId} user", orderId, AccountUserId);

            using var response = await HttpClient.GetAsync($"orders/{orderId}", cancellationToken)
                .ConfigureAwait(false);

            var responseContent = await response.Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            var orderDto = response.IsSuccessStatusCode
                ? JsonConvert.DeserializeObject<OrderDto>(responseContent, JsonSerializerSettings)
                : null;

            if (orderDto == null)
            {
                _log?.LogError("Failed to fetch user order. Response: {ResponseMessage} [{ResponseStatusCode}]", responseContent, response.StatusCode);

                return null;
            }

            return MapOrderDtoToOrder(orderDto);
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
                        _log?.LogDebug("Waiting for the authentication token to expire. Wait {Delay}", delay);

                        await Task.Delay(delay, cancellationToken)
                            .ConfigureAwait(false);

                        _log?.LogDebug("The authentication token will expire soon. Making a new request of authentication");

                        await AuthenticateAsync(cancellationToken)
                            .ConfigureAwait(false);

                        delay = GetDelay(_authenticationData!.Expires);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    _log?.LogInformation("Auto authorization has been canceled");
                }
                catch (Exception ex)
                {
                    _log?.LogError(ex, "Auto authorization failed");
                }
            },
            cancellationToken
        );

        protected virtual string GenerateClientOrderId() =>
            Guid.NewGuid().ToByteArray().ToHexString(0, 16);

        private void ClearAuthenticationData()
        {
            var currentAuthorizationHeaderExists = HttpClient.DefaultRequestHeaders.Contains("Authorization");
            _log?.LogDebug("Clearing authentication data. The current authorization header exists == {@exists}", currentAuthorizationHeaderExists);

            _authenticationData = null;
            if (currentAuthorizationHeaderExists)
                HttpClient.DefaultRequestHeaders.Remove("Authorization");
        }

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
                    _log?.LogDebug("Wait {WaitingInterval} for the swap: {@Swap}", DefaultWaitingSwapInterval, swap);

                    await Task.Delay(DefaultWaitingSwapInterval, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    _log?.LogDebug("Handle the swap without waiting: {@Swap}", swap);
                }

                SwapUpdated?.Invoke(this, new SwapEventArgs(swap));
            }
            catch (OperationCanceledException)
            {
                _log?.LogDebug("The {TaskName} task has been canceled", nameof(HandleSwapAsync));
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "Handling the swap failed: {@Swap}", swap);
            }
        }, cancellationToken);

        private bool IsNeedToWaitSwap(SwapDto swapDto)
            => swapDto.User?.Status == PartyStatus.Created || swapDto.CounterParty?.Status == PartyStatus.Created;

        private Swap MapSwapDtoToSwap(SwapDto swapDto)
        {
            var swapStatus = SwapStatus.Empty;

            if (swapDto.User?.Status > PartyStatus.Created)
                swapStatus |= (swapDto.IsInitiator ? SwapStatus.Initiated : SwapStatus.Accepted);

            if (swapDto.CounterParty?.Status > PartyStatus.Created)
                swapStatus |= (swapDto.IsInitiator ? SwapStatus.Accepted : SwapStatus.Initiated);

            return new Swap()
            {
                Id         = swapDto.Id,
                SecretHash = !string.IsNullOrWhiteSpace(swapDto.SecretHash)
                    ? Hex.FromString(swapDto.SecretHash)
                    : null,
                Status       = swapStatus,
                OrderId      = swapDto.User?.Trades?.FirstOrDefault()?.OrderId ?? 0L,
                TimeStamp    = swapDto.TimeStamp,
                Symbol       = swapDto.Symbol,
                Side         = swapDto.Side,
                Price        = swapDto.Price,
                Qty          = swapDto.Qty,
                IsInitiative = swapDto.IsInitiator,

                ToAddress       = swapDto.User?.Requisites?.ReceivingAddress,
                RewardForRedeem = swapDto.User?.Requisites?.RewardForRedeem ?? 0m,
                RefundAddress   = swapDto.User?.Requisites?.RefundAddress,

                PartyAddress         = swapDto.CounterParty?.Requisites?.ReceivingAddress,
                PartyRewardForRedeem = swapDto.CounterParty?.Requisites?.RewardForRedeem ?? 0m,
                PartyRefundAddress   = swapDto.CounterParty?.Requisites?.RefundAddress
            };
        }

        private Order MapOrderDtoToOrder(OrderDto orderDto)
        {
            var lastQty = orderDto?.Trades?.Sum(t => t.Qty) ?? 0m;
            var lastPrice = lastQty > 0m
                ? (orderDto?.Trades?.Sum(t => t.Qty * t.Price) ?? 0m) / lastQty
                : 0m;

            return new Order
            {
                Id            = orderDto.Id,
                ClientOrderId = orderDto.ClientOrderId,
                Symbol        = orderDto.Symbol,
                Side          = orderDto.Side,
                Price         = orderDto.Price,
                Status        = orderDto.Status,
                TimeStamp     = orderDto.TimeStamp,
                Qty           = orderDto.Qty,
                LeaveQty      = orderDto.LeaveQty,
                Type          = orderDto.Type,
                LastQty       = lastQty,
                LastPrice     = lastPrice
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Exception GetAuthenticationFailedException() => new("Authentication is failed");

        private static async Task<string> ConvertQueryParamsToStringAsync(Dictionary<string, string> urlParams)
        {
            using var content = new FormUrlEncodedContent(urlParams);

            return await content
                .ReadAsStringAsync()
                .ConfigureAwait(false);
        }
    }
}