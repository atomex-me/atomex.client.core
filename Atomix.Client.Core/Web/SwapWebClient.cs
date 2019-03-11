using System;
using System.IO;
using Atomix.Api.Proto;
using Atomix.Common.Proto;
using Atomix.Core;
using Atomix.Swaps;
using Atomix.Swaps.Abstract;
using Microsoft.Extensions.Configuration;

namespace Atomix.Web
{
    public class SwapWebClient : BinaryWebSocketClient, ISwapClient
    {
        public event EventHandler<SwapDataEventArgs> SwapDataReceived;

        public SwapWebClient(IConfiguration configuration)
            : this(configuration["Swap:Url"])
        {
        }

        public SwapWebClient(string url)
            : base(url)
        {
            AddHandler(SwapDataScheme.MessageId, OnSwapDataHandler);
        }

        protected void OnSwapDataHandler(MemoryStream stream)
        {
            var swapData = stream.Deserialize<SwapData>(ProtoScheme.Swap);
            SwapDataReceived?.Invoke(this, new SwapDataEventArgs(swapData));
        }

        public void AuthAsync(Auth auth)
        {
            SendAsync(ProtoScheme.Auth.SerializeWithMessageId(auth));
        }

        public void SendSwapDataAsync(SwapData swapData)
        {
            SendAsync(ProtoScheme.Swap.SerializeWithMessageId(swapData));
        }
    }
}