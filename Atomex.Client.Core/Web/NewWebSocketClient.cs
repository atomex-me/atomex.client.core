using System;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Serilog;
using Serilog.Events;
using Websocket.Client;
using Timer = System.Timers.Timer;

namespace Atomex.Web
{
    public class NewWebSocketClient
    {
        private readonly int RECONNECT_TIMEOUT_SECONDS = 15;
        private readonly int ERROR_RECONNECT_TIMEOUT_SECONDS = 15;
        
        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public bool IsConnected => _ws.IsRunning;
        private IWebsocketClient _ws { get; set; }

        private Timer debounceDisconnected;

        private DateTime lastReconnectTime;

        private bool shouldReconnect;

        protected NewWebSocketClient(string url)
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
            lastReconnectTime = DateTime.UtcNow;

            _ws.ReconnectionHappened.Subscribe(type =>
                {
                    lastReconnectTime = DateTime.UtcNow;
                    shouldReconnect = false;
                    Connected?.Invoke(this, null);
                }
            );

            _ws.DisconnectionHappened.Subscribe(info =>
                {
                    if (!shouldReconnect && info.Type != DisconnectionType.ByUser)
                    {
                        debounceDisconnected.Stop();
                        debounceDisconnected.Start();
                    }
                    
                    shouldReconnect = true;
                    if (info.Type == DisconnectionType.Error) lastReconnectTime = DateTime.UtcNow;
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
            });
            
            debounceDisconnected = new Timer(RECONNECT_TIMEOUT_SECONDS * 1000);
            debounceDisconnected.Elapsed += DebouncedDisconnectEvent;
            debounceDisconnected.AutoReset = false;
        }

        private void DebouncedDisconnectEvent(object sender, ElapsedEventArgs e)
        {
            var lastReconnectSecondsDelta = (int)(DateTime.UtcNow - lastReconnectTime).TotalSeconds;
            
            if (lastReconnectSecondsDelta > RECONNECT_TIMEOUT_SECONDS && shouldReconnect)
            {
                _ws.Reconnect();
            }
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


        protected void SendAsync(byte[] data)
        {
            _ws.Send(data);
        }
    }
}