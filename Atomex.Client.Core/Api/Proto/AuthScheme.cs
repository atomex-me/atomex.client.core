using Atomex.Common.Proto;
using Atomex.Core;

namespace Atomex.Api.Proto
{
    public class AuthScheme : ProtoScheme<Auth>
    {
        public AuthScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(Auth), true)
                .AddRequired(nameof(Auth.TimeStamp))
                .AddRequired(nameof(Auth.Nonce))
                .AddRequired(nameof(Auth.ClientNonce))
                .AddRequired(nameof(Auth.PublicKeyHex))
                .AddRequired(nameof(Auth.Signature))
                .AddRequired(nameof(Auth.Version));
        }
    }
}
