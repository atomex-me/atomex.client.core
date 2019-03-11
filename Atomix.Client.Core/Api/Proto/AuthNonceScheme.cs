using Atomix.Common.Proto;
using Atomix.Core;

namespace Atomix.Api.Proto
{
    public class AuthNonceScheme : ProtoScheme
    {
        public const int MessageId = 0;

        public AuthNonceScheme()
            : base(MessageId)
        {
            Model.Add(typeof(AuthNonce), true)
                .AddRequired(nameof(Core.AuthNonce.Nonce));
        }
    }
}