using System;
using System.IO;
using Atomix.Api.Proto;
using Atomix.Common.Proto;
using Atomix.Core;
using WebSocketSharp;

namespace Atomix.Web
{
    public class BinaryWebSocketClient : WebSocketClient
    {
        public const int MaxHandlersCount = 16;
        private Action<MemoryStream>[] Handlers { get; } = new Action<MemoryStream>[MaxHandlersCount];

        public AuthNonce Nonce { get; protected set; }
        public event EventHandler AuthOk;
        public event EventHandler AuthNonce;
        public event EventHandler<Core.ErrorEventArgs> Error;

        public void AddHandler(byte messageId, Action<MemoryStream> handler)
        {
            Handlers[messageId] = handler;
        }

        public BinaryWebSocketClient(string url)
            : base(url)
        {
            AddHandler(AuthNonceScheme.MessageId, AuthNonceHandler);
            AddHandler(AuthOkScheme.MessageId, AuthOkHandler);
            AddHandler(ErrorScheme.MessageId, ErrorHandler);
        }

        protected override void OnBinaryMessage(object sender, MessageEventArgs args)
        {
            var stream = new MemoryStream(args.RawData);

            while (stream.Position < stream.Length)
            {
                var messageId = (byte)stream.ReadByte();

                if (messageId < Handlers.Length && Handlers[messageId] != null)
                    Handlers[messageId]?.Invoke(stream);
            }
        }

        private void AuthNonceHandler(MemoryStream stream)
        {
            Nonce = stream.Deserialize<AuthNonce>(ProtoScheme.AuthNonce);

            AuthNonce?.Invoke(this, EventArgs.Empty);
        }

        private void AuthOkHandler(MemoryStream stream)
        {
            AuthOk?.Invoke(this, EventArgs.Empty);
        }

        protected void ErrorHandler(MemoryStream stream)
        {
            var error = stream.Deserialize<Error>(ProtoScheme.Error);
            Error?.Invoke(this, new Core.ErrorEventArgs(error));
        }
    }
}