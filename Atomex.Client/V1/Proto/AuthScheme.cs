using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
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
