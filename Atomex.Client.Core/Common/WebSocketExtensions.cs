using System;
using System.Threading.Tasks;
using Serilog;
using WebSocketSharp;

namespace Atomex.Common
{
    public static class WebSocketExtensions
    {
        public static Task ConnectAsync(this WebSocket socket)
        {
            return Task.Factory.StartNew(() =>
            {
                try
                {
                    socket.Connect();
                }
                catch (Exception e)
                {
                    Log.Error("Connect async error");
                }
            });
        }

        public static Task CloseAsync(this WebSocket socket, CloseStatusCode statusCode)
        {
            return Task.Factory.StartNew(() =>
            {
                try
                {
                    socket.Close(statusCode);
                }
                catch (Exception e)
                {
                    Log.Error("Close async error");
                }
            });
        }

        public static Task SendAsync(this WebSocket socket, byte[] data)
        {
            return Task.Factory.StartNew(() =>
            {
                try
                {
                    socket.Send(data);
                }
                catch (Exception e)
                {
                    Log.Error("Send async error");
                }
            });
        }
    }
}