using Atomix.Common.Proto;
using Atomix.Core;

namespace Atomix.Api.Proto
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