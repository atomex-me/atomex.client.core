using Atomex.Common.Proto;

namespace Atomex.Api.Proto
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