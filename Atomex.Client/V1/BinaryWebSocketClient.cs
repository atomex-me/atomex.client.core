using System;
using System.IO;

using Microsoft.Extensions.Logging;

using Atomex.Client.V1.Entities;
using Atomex.Client.V1.Common;
using Atomex.Client.V1.Proto;
using Atomex.Common;
using ErrorEventArgs = Atomex.Common.ErrorEventArgs;
using Error = Atomex.Common.Error;

namespace Atomex.Client.V1
{
    public class BinaryWebSocketClient : WebSocketClient
    {
        private const int MaxHandlersCount = 32;

        public event EventHandler AuthOk;
        public event EventHandler<NonceEventArgs> AuthNonce;
        public event EventHandler<ErrorEventArgs> Error;

        private readonly Action<MemoryStream>[] _handlers;
        private readonly ILogger _log;
        protected ProtoSchemes Schemes { get; }

        public void SendHeartBeatAsync() =>
            SendAsync(Schemes.HeartBeat.SerializeWithMessageId("ping"));

        protected void AddHandler(byte messageId, Action<MemoryStream> handler)
        {
            _handlers[messageId] = handler;
        }

        protected BinaryWebSocketClient(string url, ProtoSchemes schemes, ILogger log = null)
            : base(url)
        {
            _handlers = new Action<MemoryStream>[MaxHandlersCount];
            _log = log;
            Schemes = schemes;

            AddHandler(Schemes.AuthNonce.MessageId, AuthNonceHandler);
            AddHandler(Schemes.AuthOk.MessageId, AuthOkHandler);
            AddHandler(Schemes.Error.MessageId, ErrorHandler);
            AddHandler(Schemes.HeartBeat.MessageId, HeartBeatHandler);
        }

        protected override void OnBinaryMessage(byte[] data)
        {
            using var stream = new MemoryStream(data);

            while (stream.Position < stream.Length)
            {
                var messageId = (byte)stream.ReadByte();

                if (messageId < _handlers.Length && _handlers[messageId] != null)
                    _handlers[messageId]?.Invoke(stream);
            }
        }

        private void AuthNonceHandler(MemoryStream stream)
        {
            var nonce = Schemes.AuthNonce.DeserializeWithLengthPrefix(stream);

            AuthNonce?.Invoke(this, new NonceEventArgs(nonce));
        }

        private void AuthOkHandler(MemoryStream stream)
        {
            var authOk = Schemes.AuthOk.DeserializeWithLengthPrefix(stream);

            AuthOk?.Invoke(this, EventArgs.Empty);
        }

        private void ErrorHandler(MemoryStream stream)
        {
            var error = Schemes.Error.DeserializeWithLengthPrefix(stream);

            Error?.Invoke(this, new ErrorEventArgs(new Error(error.Code, error.Description)));
        }

        private void HeartBeatHandler(MemoryStream stream)
        {
            var pong = Schemes.HeartBeat.DeserializeWithLengthPrefix(stream);

            if (pong.ToLowerInvariant() != "pong")
                _log.LogError("Invalid heart beat response");
        }
    }
}