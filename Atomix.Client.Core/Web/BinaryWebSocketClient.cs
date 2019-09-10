using System;
using System.IO;
using Atomix.Api.Proto;
using Atomix.Core;
using WebSocketSharp;

namespace Atomix.Web
{
    public class BinaryWebSocketClient : WebSocketClient
    {
        private const int MaxHandlersCount = 32;
        private Action<MemoryStream>[] Handlers { get; } = new Action<MemoryStream>[MaxHandlersCount];
        protected ProtoSchemes Schemes { get; }

        public AuthNonce Nonce { get; private set; }
        public event EventHandler AuthOk;
        public event EventHandler AuthNonce;
        public event EventHandler<Core.ErrorEventArgs> Error;

        protected void AddHandler(byte messageId, Action<MemoryStream> handler)
        {
            Handlers[messageId] = handler;
        }

        protected BinaryWebSocketClient(string url, ProtoSchemes schemes)
            : base(url)
        {
            Schemes = schemes;

            AddHandler(Schemes.AuthNonce.MessageId, AuthNonceHandler);
            AddHandler(Schemes.AuthOk.MessageId, AuthOkHandler);
            AddHandler(Schemes.Error.MessageId, ErrorHandler);
        }

        protected override void OnBinaryMessage(object sender, MessageEventArgs args)
        {
            using (var stream = new MemoryStream(args.RawData))
            {
                while (stream.Position < stream.Length)
                {
                    var messageId = (byte) stream.ReadByte();

                    if (messageId < Handlers.Length && Handlers[messageId] != null)
                        Handlers[messageId]?.Invoke(stream);
                }
            }
        }

        private void AuthNonceHandler(MemoryStream stream)
        {
            Nonce = Schemes.AuthNonce.DeserializeWithLengthPrefix(stream);

            AuthNonce?.Invoke(this, EventArgs.Empty);
        }

        private void AuthOkHandler(MemoryStream stream)
        {
            var authOk = Schemes.AuthOk.DeserializeWithLengthPrefix(stream);

            AuthOk?.Invoke(this, EventArgs.Empty);
        }

        private void ErrorHandler(MemoryStream stream)
        {
            var error = Schemes.Error.DeserializeWithLengthPrefix(stream);

            Error?.Invoke(this, new Core.ErrorEventArgs(error));
        }
    }
}