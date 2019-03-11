using Atomix.Common.Proto;
using Atomix.Core;

namespace Atomix.Api.Proto
{
    public class AuthScheme : ProtoScheme
    {
        public const int MessageId = 1;

        public AuthScheme()
            : base(MessageId)
        {
            Model.Add(typeof(Auth), true)
                .AddRequired(nameof(Core.Auth.TimeStamp))
                .AddRequired(nameof(Core.Auth.Nonce))
                .AddRequired(nameof(Core.Auth.ClientNonce))
                .AddRequired(nameof(Core.Auth.PublicKeyHex))
                .AddRequired(nameof(Core.Auth.Signature));
        }
    }
}
