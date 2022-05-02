using Atomex.Client.V1.Common;
using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
{
    public class AuthNonceScheme : ProtoScheme<AuthNonce>
    {
        public AuthNonceScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(AuthNonce), true)
                .AddRequired(nameof(AuthNonce.Nonce))
                .AddRequired(nameof(AuthNonce.Version));
        }
    }
}