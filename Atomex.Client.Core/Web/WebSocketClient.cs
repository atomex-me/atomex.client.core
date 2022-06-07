using System;
using System.Net.WebSockets;
using System.Threading.Tasks;

using Serilog;
using Websocket.Client;

using Atomex.Common;

namespace Atomex.Web
{
    public class WebSocketClient
    {
        private const int RECONNECT_TIMEOUT_SECONDS = 18;
        private const int ERROR_RECONNECT_TIMEOUT_SECONDS = 15;
        private const int KEEP_ALIVE_SECONDS = 5;
        
        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<ResponseMessage> OnMessage;
        public bool IsConnected => _ws.IsRunning;
        private readonly IWebsocketClient _ws;
        
        public WebSocketClient(
            string url,
            HttpRequestHeaders headers = null,
            int keepAliveInSec = KEEP_ALIVE_SECONDS)
        {
            var factory = new Func<ClientWebSocket>(() =>
            {
                var client = new ClientWebSocket
                {
                    Options = { KeepAliveInterval = TimeSpan.FromSeconds(keepAliveInSec) }
                };

                if (headers != null)
                    foreach (var header in headers)
                        foreach (var value in header.Value)
                            client.Options.SetRequestHeader(header.Key, value); // TODO: check multi-value case

                return client;
            });

            _ws = new WebsocketClient(new Uri(url), factory)
            {
                Name = url,
                ReconnectTimeout = TimeSpan.FromSeconds(RECONNECT_TIMEOUT_SECONDS),
                ErrorReconnectTimeout = TimeSpan.FromSeconds(ERROR_RECONNECT_TIMEOUT_SECONDS),
            };

            _ws.ReconnectionHappened.Subscribe(type =>
            {
                Log.Debug($"WebSocket {_ws.Url} opened");
                Connected?.Invoke(this, null);
            });

            _ws.DisconnectionHappened.Subscribe(info =>
            {
                Log.Debug($"WebSocket {_ws.Url } closed");
                Disconnected?.Invoke(this, null);
            });

            _ws.MessageReceived.Subscribe(msg =>
            {
                if (msg.MessageType == WebSocketMessageType.Binary)
                {
                    OnBinaryMessage(msg.Binary);
                }
                else if (msg.MessageType == WebSocketMessageType.Text)
                {
                    OnTextMessage(msg.Text);
                }
                else
                {
                    throw new NotSupportedException("Unsupported web socket message type");
                }

                OnMessage?.Invoke(this, msg);
            });
        }

        protected virtual void OnTextMessage(string data)
        {
        }

        protected virtual void OnBinaryMessage(byte[] data)
        {
        }

        public Task ConnectAsync() =>
            _ws.Start();

        public Task CloseAsync() =>
            _ws.Stop(
                WebSocketCloseStatus.NormalClosure,
                string.Empty);

        public void Send(string data) =>
            _ws.Send(data);

        protected void SendAsync(byte[] data) =>
            _ws.Send(data);
    }
}