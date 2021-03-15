using System;
using System.Threading;
using System.Threading.Tasks;

using Serilog;
using WebSocketSharp;

using Atomex.Common;

namespace Atomex.Web
{
    public class WebSocketClient : IDisposable
    {
        private readonly WebSocket _ws;
        private int _reconnectAttempts;
        private CancellationTokenSource _reconnectCts;

        public event EventHandler Connected;
        public event EventHandler<CloseEventArgs> Disconnected;
        public bool IsConnected => _ws.ReadyState == WebSocketState.Open;

        private bool Reconnection { get; } = true;
        private TimeSpan ReconnectionDelayMin { get; } = TimeSpan.FromSeconds(1);
        private TimeSpan ReconnectionDelayMax { get; } = TimeSpan.FromSeconds(10); //TimeSpan.FromMinutes(1);
        private int ReconnectionAttempts { get; } = int.MaxValue;
        private int ReconnectionAttemptWithMaxDelay { get; } = 10; //20;

        protected WebSocketClient(string url)
        {
            _ws = new WebSocket(url) {Log = {Output = (data, s) => { }}};
            _ws.OnOpen += OnOpenEventHandler;
            _ws.OnClose += OnCloseEventHandler;
            _ws.OnError += OnErrorEventHandler;
            _ws.OnMessage += OnMessageEventHandler;
        }

        private void OnOpenEventHandler(object sender, EventArgs args)
        {
            Log.Debug("WebSocket opened.");

            Connected?.Invoke(this, args);

            _reconnectAttempts = 0;

            if (_reconnectCts != null) {
                _reconnectCts.Cancel();
                _reconnectCts = null;
            }
        }

        private void OnCloseEventHandler(object sender, CloseEventArgs args)
        {
            Log.Debug("WebSocket closed.");

            Disconnected?.Invoke(this, args);

            if (args.Code != (ushort) CloseStatusCode.Normal)
                TryToReconnect();
        }

        private void OnErrorEventHandler(object sender, ErrorEventArgs args)
        {
            Log.Error(args.Exception, "Socket error: {@message}", args.Message);
        }

        private void OnMessageEventHandler(object sender, MessageEventArgs args)
        {
            if (args.IsBinary) {
                OnBinaryMessage(sender, args);
            } else if (args.IsText) {
                OnTextMessage(sender, args);
            } else {
                throw new NotSupportedException("Unsupported web socket message type");
            }
        }

        private void OnTextMessage(object sender, MessageEventArgs args)
        {
        }

        protected virtual void OnBinaryMessage(object sender, MessageEventArgs args)
        {
        }

        public void Connect()
        {
            _ws.Connect();
        }

        public Task ConnectAsync()
        {
            //_ws.ConnectAsync();
            return WebSocketExtensions.ConnectAsync(_ws);
        }

        public void Close()
        {
            if (IsConnected)
                _ws.Close(CloseStatusCode.Normal);
        }

        public Task CloseAsync()
        {
            //_ws.CloseAsync(CloseStatusCode.Normal);
            return WebSocketExtensions.CloseAsync(_ws, CloseStatusCode.Normal);
        }

        //public void Connect(string userName, string password)
        //{
        //    _ws.SetCredentials(userName, password, true);
        //    _ws.Connect();
        //}

        protected void SendAsync(byte[] data)
        {
            _ = _ws.SendAsync(data);
        }

        private async void TryToReconnect()
        {
            if (!Reconnection)
                return;

            if (ReconnectionAttempts != int.MaxValue && _reconnectAttempts >= ReconnectionAttempts) {
                Log.Debug("Reconnection attempts exhausted");
                return;
            }

            var reconnectInterval = GetReconnectInterval(_reconnectAttempts);

            try
            {
                Log.Debug("Try to reconnect through {@interval}, attempt: {@attempt}", reconnectInterval,
                    _reconnectAttempts);

                _reconnectCts = new CancellationTokenSource();

                await Task.Delay(reconnectInterval, _reconnectCts.Token)
                    .ConfigureAwait(false);//, );

                if (_ws.ReadyState != WebSocketState.Connecting && _ws.ReadyState != WebSocketState.Open)
                {
                    _reconnectAttempts++;

                    _ = ConnectAsync();
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("Reconnection was canceled");
            }
            catch (Exception e)
            {
                Log.Error("Reconnect error");
            }
        }

        private TimeSpan GetReconnectInterval(int attemptNumber)
        {
            var incrementInTicks = ReconnectionDelayMax.Ticks / ReconnectionAttemptWithMaxDelay;
            var intervalInTicks = Math.Max(ReconnectionDelayMin.Ticks, attemptNumber * incrementInTicks);

            return TimeSpan.FromTicks(intervalInTicks);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
                _ws.Close();
        }

        ~WebSocketClient()
        {
            Dispose(false);
        }
    }
}