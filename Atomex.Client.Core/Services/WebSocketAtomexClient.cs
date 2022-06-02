using System;
using System.Text.Json;
using System.Threading.Tasks;

using Websocket.Client;

using Atomex.Core;
using Atomex.MarketData;
using Atomex.MarketData.Abstract;
using Atomex.Services.Abstract;
using Atomex.Swaps;
using Atomex.Wallet.Abstract;
using Atomex.Web;

namespace Atomex.Services
{
    public class WebSocketAtomexClient : IAtomexClient
    {
        public event EventHandler<AtomexClientServiceEventArgs> ServiceConnected;
        public event EventHandler<AtomexClientServiceEventArgs> ServiceDisconnected;
        public event EventHandler<AtomexClientServiceEventArgs> ServiceAuthenticated;
        public event EventHandler<AtomexClientErrorEventArgs> Error;
        public event EventHandler<OrderEventArgs> OrderReceived;
        public event EventHandler<SwapEventArgs> SwapReceived;
        public event EventHandler<MarketDataEventArgs> QuotesUpdated;

        public IAccount Account { get; private set; }
        public IMarketDataRepository MarketDataRepository { get; private set; }

        private readonly string _exchangeUrl;
        private readonly string _marketDataUrl;
        private WebSocketClient _exchangeWs;
        private WebSocketClient _marketDataWs;

        public WebSocketAtomexClient(
            string exchangeUrl,
            string marketDataUrl)
        {
            _exchangeUrl = exchangeUrl;
            _marketDataUrl = marketDataUrl;
        }

        public bool IsServiceConnected(AtomexClientService service) =>
            service switch
            {
                AtomexClientService.Exchange => _exchangeWs.IsConnected,
                AtomexClientService.MarketData => _marketDataWs.IsConnected,
                AtomexClientService.All => _exchangeWs.IsConnected && _marketDataWs.IsConnected,
                _ => throw new ArgumentOutOfRangeException(nameof(service), service, null)
            };

        public Task StartAsync()
        {
            _exchangeWs = new WebSocketClient(_exchangeUrl);

            _exchangeWs.Connected += ExchangeConnected;
            _exchangeWs.Disconnected += ExchangeDisconnected;
            _exchangeWs.OnMessage += ExchangeOnMessage;

            _marketDataWs = new WebSocketClient(_marketDataUrl);

            _marketDataWs.Connected += MarketDataConnected;
            _marketDataWs.Disconnected += MarketDataDisconnected;
            _marketDataWs.OnMessage += MarketDataOnMessage;

            return Task.WhenAll(
                _exchangeWs.ConnectAsync(),
                _marketDataWs.ConnectAsync());
        }

        public Task StopAsync()
        {
            return Task.WhenAll(
                _exchangeWs.CloseAsync(),
                _marketDataWs.CloseAsync());
        }

        private void ExchangeConnected(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void ExchangeDisconnected(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void ExchangeOnMessage(object sender, ResponseMessage e)
        {
            throw new NotImplementedException();
        }

        private void MarketDataConnected(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void MarketDataDisconnected(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void MarketDataOnMessage(object sender, ResponseMessage e)
        {
            throw new NotImplementedException();
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

            _exchangeWs.Send(JsonSerializer.Serialize(request));
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

            _exchangeWs.Send(JsonSerializer.Serialize(request));
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

            _marketDataWs.Send(JsonSerializer.Serialize(request));
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
    }
}