using Atomex.Abstract;
using Atomex.Common;
using Atomex.Core;
using Atomex.Cryptography.Abstract;
using Atomex.MarketData;
using Atomex.MarketData.Abstract;
using Atomex.Services.Abstract;
using Atomex.Swaps;
using Atomex.Wallet.Abstract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

#nullable enable

namespace Atomex.Services
{
    public class RestAtomexClient : IAtomexClient
    {
        const string DEFAULT_AUTHENTICATION_MESSAGE = "Signing in ";
        const uint DEFAULT_AUTHENTICATION_ACCOUNT_INDEX = 0u;

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
        private bool _isConnected = false;
        private AuthenticationData? _authenticationData;

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

        public bool IsServiceConnected(AtomexClientService service) => service switch
        {
            AtomexClientService.Exchange or AtomexClientService.MarketData or AtomexClientService.All => _isConnected,
            _ => throw new ArgumentOutOfRangeException(nameof(service), service, null)
        };

        public async Task StartAsync()
        {
            try
            {
                Logger.LogInformation("{atomexClientName} is starting for the {userId} [{network}] user", nameof(RestAtomexClient), AccountUserId, Account.Network);

                MarketDataRepository.Initialize(SymbolsProvider.GetSymbols(Account.Network));
                Logger.LogDebug("MarketDataRepository is initialized");

                var isAuthenticated = await AuthenticateAsync();
                if (!isAuthenticated)
                    throw new Exception("Authentication is failed");

                ServiceConnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));
                ServiceConnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));

                _isConnected = true;
                Logger.LogInformation("{atomexClientName} has been started for the {userId} [{network}] user", nameof(RestAtomexClient), AccountUserId, Account.Network);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An exception has been occurred when the {atomexClientName} client is started for the {userId} [{network}] user",
                    nameof(RestAtomexClient), AccountUserId, Account.Network);
            }
        }

        public async Task StopAsync()
        {
            try
            {
                Logger.LogInformation("{atomexClientName} is stopping for the {userId} [{network}] user", nameof(RestAtomexClient), AccountUserId, Account.Network);

                ServiceDisconnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.MarketData));
                ServiceDisconnected?.Invoke(this, new AtomexClientServiceEventArgs(AtomexClientService.Exchange));

                MarketDataRepository.Clear();
                Logger.LogDebug("MarketDataRepository has been cleared");

                _isConnected = false;
                Logger.LogInformation("{atomexClientName} has been stopped for the {userId} [{network}] user", nameof(RestAtomexClient), AccountUserId, Account.Network);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An exception has been occurred when the {atomexClientName} client is stopped for the {userId} [{network}] user",
                    nameof(RestAtomexClient), AccountUserId, Account.Network);
            }
        }

        public MarketDataOrderBook GetOrderBook(string symbol) => MarketDataRepository.OrderBookBySymbol(symbol);

        public MarketDataOrderBook GetOrderBook(Symbol symbol) => MarketDataRepository.OrderBookBySymbol(symbol.Name);

        public Quote GetQuote(Symbol symbol) => MarketDataRepository.QuoteBySymbol(symbol.Name);

        public void OrderCancelAsync(long id, string symbol, Side side)
        {
            throw new NotImplementedException();
        }

        public void OrderSendAsync(Order order)
        {
            throw new NotImplementedException();
        }

        public void SubscribeToMarketData(SubscriptionType type)
        {
            throw new NotImplementedException();
        }

        public void SwapAcceptAsync(long id, string symbol, string toAddress, decimal rewardForRedeem, string refundAddress)
        {
            throw new NotImplementedException();
        }

        public void SwapInitiateAsync(long id, byte[] secretHash, string symbol, string toAddress, decimal rewardForRedeem, string refundAddress)
        {
            throw new NotImplementedException();
        }

        public void SwapStatusAsync(string requestId, long swapId)
        {
            throw new NotImplementedException();
        }

        protected async Task<bool> AuthenticateAsync()
        {
            using var securePublicKey = Account.Wallet.GetServicePublicKey(AuthenticationAccountIndex);
            var publicKey = securePublicKey.ToUnsecuredBytes();

            var timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var signingMessage = AuthenticationMessage + timeStamp.ToString();
            var signingMessagePayload = HashAlgorithm.Sha256.Hash(
                BitcoinUtils.FormatMessageForSigning(Encoding.UTF8.GetBytes(signingMessage)),
                iterations: 2
            );
            var signature = await Account.Wallet
                .SignByServiceKeyAsync(signingMessagePayload, AuthenticationAccountIndex)
                .ConfigureAwait(false);

            var authenticationRequestContent = new AuthenticationRequestContent(
                Message: AuthenticationMessage,
                TimeStamp: timeStamp,
                PublicKey: Hex.ToHexString(publicKey),
                Signature: Hex.ToHexString(signature),
                Algorithm: "Sha256WithEcdsa:BtcMsg"
            );

            var response = await HttpClient
                .PostAsync(
                    "token",
                    new StringContent(
                        content: JsonConvert.SerializeObject(authenticationRequestContent, JsonSerializerSettings),
                        encoding: Encoding.UTF8,
                        mediaType: "application/json"
                    )
                ).ConfigureAwait(false);

            var responseContent = await response.Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogError("Authentication is failed for the {userId} user. Response: {responseMessage} [{responseStatusCode}]", AccountUserId, responseContent, response.StatusCode);
                return false;
            }

            var authenticationData = JsonConvert.DeserializeObject<AuthenticationData>(responseContent, JsonSerializerSettings);
            if (authenticationData == null)
            {
                Logger.LogError("Authentication is failed for the {userId} user. It's not possible to parse authentication data", AccountUserId);
                return false;
            }
            if (string.IsNullOrWhiteSpace(authenticationData.Token))
            {
                Logger.LogError("Authentication is failed for the {userId} user. Authentication token is invalid", AccountUserId);
                return false;
            }

            _authenticationData = authenticationData;

            if (HttpClient.DefaultRequestHeaders.Contains("Authorization"))
                HttpClient.DefaultRequestHeaders.Remove("Authorization");

            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authenticationData.Token}");

            Logger.LogInformation("The {userId} user is authenticated until {authTokenExpiredDate}",
                AccountUserId, DateTimeOffset.FromUnixTimeMilliseconds(authenticationData.Expires).UtcDateTime);

            return true;
        }

        private record AuthenticationRequestContent(
            string Message,
            long TimeStamp,
            string PublicKey,
            string Signature,
            string Algorithm
        );

        private record AuthenticationData(string Id, string Token, long Expires);
    }
}
