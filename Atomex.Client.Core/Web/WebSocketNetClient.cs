using System;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Common;
using Serilog;
using WebSocketSharp;
using System.Net.WebSockets;
using System.Linq;

namespace Atomex.Web
{
    public class WebSocketNetClient : IDisposable
    {
        private readonly WebSocketWrapper _ws;
        private int _reconnectAttempts;
        private CancellationTokenSource _reconnectCts;

        public event EventHandler Connected;
        public event EventHandler<CloseEventArgs> Disconnected;
        public bool IsConnected => _ws.IsConnected;

        private bool Reconnection { get; } = true;
        private TimeSpan ReconnectionDelayMin { get; } = TimeSpan.FromSeconds(1);
        private TimeSpan ReconnectionDelayMax { get; } = TimeSpan.FromSeconds(10); //TimeSpan.FromMinutes(1);
        private int ReconnectionAttempts { get; } = int.MaxValue;
        private int ReconnectionAttemptWithMaxDelay { get; } = 10; //20;
 
        protected WebSocketNetClient(string url)
        {
            _ws = WebSocketWrapper.Create(url);

            _ws.OnConnect(OnOpenEventHandler);
            _ws.OnDisconnect(OnCloseEventHandler);
            _ws.OnMessage(OnMessageEventHandler);
        }

        private void OnOpenEventHandler(WebSocketWrapper ws)
        { 
          Connected?.Invoke(this, null);
          _reconnectAttempts = 0;

          if (_reconnectCts != null) {
              _reconnectCts.Cancel();
              _reconnectCts = null;
          }
        }

        private void OnCloseEventHandler(WebSocketWrapper ws)
        {
            Disconnected?.Invoke(this, null);
            TryToReconnect();
        }

        private void OnMessageEventHandler(WebSocketWrapper ws, byte[] rawRata)
        {
          OnBinaryMessage(null, rawRata);
        }

        protected virtual void OnBinaryMessage(object sender, byte[] RawData)
        {
        }

        public Task ConnectAsync()
        {
          return _ws.Connect();
        }

        public Task CloseAsync()
        {
            return _ws.Close();
        }

        protected void SendAsync(byte[] data)
        {
            _ws.SendBytes(data);
        }

        private async void TryToReconnect()
        {
            if (!Reconnection)
                return;

            if (ReconnectionAttempts != int.MaxValue && _reconnectAttempts >= ReconnectionAttempts) {
                Log.Debug("Reconnection attempts exhausted");
                return;
            }

            while (_ws.ReadyState != CustomStates.Open)
            {
                _reconnectAttempts++;
                var reconnectInterval = GetReconnectInterval(_reconnectAttempts);

                try
                {
                    Log.Debug("Try to reconnect through {@interval}, attempt: {@attempt}", reconnectInterval,
                        _reconnectAttempts);

                    _reconnectCts = new CancellationTokenSource();

                    await Task.Delay(reconnectInterval, _reconnectCts.Token)
                        .ConfigureAwait(false);


                        // Log.Error("Try to reconnect through {@interval}, attempt: {@attempt}", reconnectInterval,
                        // _reconnectAttempts);
                        _ = ConnectAsync();
                    }
                catch (OperationCanceledException)
                {
                    //Log.Error("WS Reconnection was canceled");
                }
                catch (Exception e)
                {
                    Log.Error("WS Reconnect error");
                }
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
                _ws.Dispose();
        }

        ~WebSocketNetClient()
        {
            Dispose(false);
        }
    }
}