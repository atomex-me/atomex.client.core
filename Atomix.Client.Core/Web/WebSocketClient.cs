using System;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Common;
using Serilog;
using WebSocketSharp;

namespace Atomix.Web
{
    public class WebSocketClient : IDisposable
    {
        private readonly WebSocket _ws;
        private int _reconnectAttempts;
        private CancellationTokenSource _reconnectCts;

        public event EventHandler Connected;
        public event EventHandler<CloseEventArgs> Disconnected;
        public bool IsConnected => _ws.ReadyState == WebSocketState.Open;

        public bool Reconnection { get; set; } = true;
        public TimeSpan ReconnectionDelayMin { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan ReconnectionDelayMax { get; set; } = TimeSpan.FromSeconds(10); //TimeSpan.FromMinutes(1);
        public int ReconnectionAttempts { get; set; } = int.MaxValue;
        public int ReconnectionAttemptWithMaxDelay { get; set; } = 10; //20;

        public WebSocketClient(string url)
        {
            _ws = new WebSocket(url) {Log = {Output = (data, s) => { }}};
            _ws.OnOpen += OnOpenEventHandler;
            _ws.OnClose += OnCloseEventHandler;
            _ws.OnError += OnErrorEventHandler;
            _ws.OnMessage += OnMessageEventHandler;
        }

        protected virtual void OnOpenEventHandler(object sender, EventArgs args)
        {  
            Connected?.Invoke(this, args);

            _reconnectAttempts = 0;

            if (_reconnectCts != null) {
                _reconnectCts.Cancel();
                _reconnectCts = null;
            }
        }

        protected virtual void OnCloseEventHandler(object sender, CloseEventArgs args)
        {
            Disconnected?.Invoke(this, args);

            if (args.Code != (ushort) CloseStatusCode.Normal)
                TryToReconnect();
        }

        protected virtual void OnErrorEventHandler(object sender, ErrorEventArgs args)
        {
            Log.Error(args.Exception, "Socket error: {@message}", args.Message);
        }

        protected virtual void OnMessageEventHandler(object sender, MessageEventArgs args)
        {
            if (args.IsBinary) {
                OnBinaryMessage(sender, args);
            } else if (args.IsText) {
                OnTextMessage(sender, args);
            } else {
                throw new NotSupportedException("Unsupported web socket message type");
            }
        }

        protected virtual void OnTextMessage(object sender, MessageEventArgs args)
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
            _ws.SendAsync(data).FireAndForget();
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

                    ConnectAsync().FireAndForget();
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("Reconnection was canceled");
            }
            catch (Exception e)
            {
                Log.Error(e, "Reconnect error");
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

        protected virtual void Dispose(bool disposing)
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