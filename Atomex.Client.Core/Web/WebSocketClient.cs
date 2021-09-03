using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Serilog;
using Websocket.Client;

namespace Atomex.Web
{
    public class WebSocketClient
    {
        private readonly int RECONNECT_TIMEOUT_SECONDS = 18;
        private readonly int ERROR_RECONNECT_TIMEOUT_SECONDS = 15;
        
        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<ResponseMessage> OnMessage;
        public bool IsConnected => _ws.IsRunning;
        private IWebsocketClient _ws { get; }

        
        public WebSocketClient(string url)
        {
            var factory = new Func<ClientWebSocket>(() =>
            {
                var client = new ClientWebSocket
                {
                    Options =
                    {
                        KeepAliveInterval = TimeSpan.FromSeconds(5)
                    }
                };
                return client;
            });

            _ws = new WebsocketClient(new Uri(url), factory);
            _ws.Name = url;
            _ws.ReconnectTimeout = TimeSpan.FromSeconds(RECONNECT_TIMEOUT_SECONDS);
            _ws.ErrorReconnectTimeout = TimeSpan.FromSeconds(ERROR_RECONNECT_TIMEOUT_SECONDS);

            _ws.ReconnectionHappened.Subscribe(type =>
                {
                    Log.Debug($"WebSocket {_ws.Url} opened.");
                    Connected?.Invoke(this, null);
                }
            );

            _ws.DisconnectionHappened.Subscribe(info =>
                {
                    Log.Debug($"WebSocket {_ws.Url } closed.");
                    Disconnected?.Invoke(this, null);
                }
            );

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

        private void OnTextMessage(string data)
        {
        }

        protected virtual void OnBinaryMessage(byte[] data)
        {
        }

        public Task ConnectAsync()
        {
            return _ws.Start();
        }

        public Task CloseAsync()
        {
            return _ws.Stop(WebSocketCloseStatus.NormalClosure, string.Empty);
        }

        public void Send(string data)
        {
            _ws.Send(data);
        }

        protected void SendAsync(byte[] data)
        {
            _ws.Send(data);
        }
    }
}