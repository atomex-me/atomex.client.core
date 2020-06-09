using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Serilog;

namespace Atomex.Web
{
    public class WebSocketWrapper
    {
        private const int ReceiveChunkSize = 512;
        private const int SendChunkSize = 512;
        private ClientWebSocket _ws;
        private readonly Uri _uri;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        private Action<WebSocketWrapper> _onConnected;
        private Action<WebSocketWrapper, byte[]> _onMessage;
        private Action<WebSocketWrapper> _onDisconnected;

        protected WebSocketWrapper(string uri)
        {
            _uri = new Uri(uri);
            _cancellationToken = _cancellationTokenSource.Token;
        }

        public bool IsConnected {
          get => _ws.State == WebSocketState.Open;
        }

        public WebSocketState ReadyState {
            get => _ws.State;
        }

        public static WebSocketWrapper Create(string uri)
        {
            return new WebSocketWrapper(uri);
        }

        public async Task<WebSocketWrapper> Connect()
        {
            await ConnectAsync();
            return this;
        }

        public async Task<WebSocketWrapper> Close() {
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            return this;
        }

        public WebSocketWrapper OnConnect(Action<WebSocketWrapper> onConnect)
        {
            _onConnected = onConnect;
            return this;
        }

        public WebSocketWrapper OnDisconnect(Action<WebSocketWrapper> onDisconnect)
        {
            _onDisconnected = onDisconnect;
            return this;
        }

        public WebSocketWrapper OnMessage(Action<WebSocketWrapper, byte[]> onMessage)
        {
            _onMessage = onMessage;
            return this;
        }

        public void SendMessage(string message)
        {
            SendMessageAsync(message);
        }

        private async void SendMessageAsync(string message)
        {
            if (_ws.State != WebSocketState.Open)
            {
                throw new Exception("Connection is not open.");
            }

            var messageBuffer = Encoding.UTF8.GetBytes(message);
            var messagesCount = (int)Math.Ceiling((double)messageBuffer.Length / SendChunkSize);

            for (var i = 0; i < messagesCount; i++)
            {
                var offset = (SendChunkSize * i);
                var count = SendChunkSize;
                var lastMessage = ((i + 1) == messagesCount);

                if ((count * (i + 1)) > messageBuffer.Length)
                {
                    count = messageBuffer.Length - offset;
                }

                await _ws.SendAsync(new ArraySegment<byte>(messageBuffer, offset, count), WebSocketMessageType.Text, lastMessage, _cancellationToken);
            }
        }

        private async Task ConnectAsync()
        {
            try {
                _ws = new ClientWebSocket();
                _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
                await _ws.ConnectAsync(_uri, _cancellationToken);
                CallOnConnected();
                StartListen();
            } catch (Exception) {
                Log.Error($"Unavailable to connect to {_uri.ToString()}");
            }

        }


        public void SendBytes(byte[] bytes)
        {
            try {
                SendBytesAsync(bytes);
            } catch(Exception e) {
                Log.Error($"Unable to send bytes with WS: {e.ToString()}");
            }
        }

        private bool disconnectFromSend = false;
        private async void SendBytesAsync(byte[] bytes)
        {
            if (_ws.State != WebSocketState.Open)
            {
                if (!disconnectFromSend)
                {
                    disconnectFromSend = true;
                    CallOnDisconnected();
                }
               return;
            }
            var messageBuffer = bytes;
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, _cancellationToken);
        }

        private async void StartListen()
        {
            var rcvBytes = new byte[ReceiveChunkSize];
            var rcvBuffer = new ArraySegment<byte>(rcvBytes);
            byte[] msgBytes;

            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    var stringResult = new StringBuilder();
                    WebSocketReceiveResult result;
                    do
                    {
                        var timeOut = new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
                        result = await _ws.ReceiveAsync(rcvBuffer, timeOut);
                        msgBytes = rcvBuffer.Skip(rcvBuffer.Offset).Take(result.Count).ToArray();

                        if (result.MessageType == WebSocketMessageType.Close && _ws.State == WebSocketState.Open)
                        {
                            CallOnDisconnected();
                            await
                                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                        }
                        else
                        {
                            var str = Encoding.UTF8.GetString(msgBytes, 0, result.Count);
                            stringResult.Append(str);
                        }

                    } while (!result.EndOfMessage);

                    rcvBytes = new byte[ReceiveChunkSize];
                    rcvBuffer = new ArraySegment<byte>(rcvBytes);

                    CallOnMessage(msgBytes);

                }
            }
            catch (OperationCanceledException) {
                 if (_ws.State == WebSocketState.Open) {
                    // Console.WriteLine("WS DISCONNECTED DUE TO INACTIVITY");
                    CallOnDisconnected();
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                }
            }
            catch (Exception)
            {
                if (_ws.State == WebSocketState.Open) {
                    // Console.WriteLine("Call disconnect from exception");
                    CallOnDisconnected();
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                }
            }
        }

        public void Dispose() {
            _ws.Dispose();
        }

        private void CallOnMessage(byte[] data)
        {
            if (_onMessage != null) {
                _onMessage(this, data);
            }
        }

        private void CallOnDisconnected()
        {
            if (_onDisconnected != null)
                _onDisconnected(this);
        }

        private void CallOnConnected()
        {
            if (_onConnected != null) {
                _onConnected(this);
            }
            Console.WriteLine("WS CONNECTED");
            disconnectFromSend = false;
        }

        private static void RunInTask(Action action)
        {
            Task.Factory.StartNew(action);
        }
    }
}