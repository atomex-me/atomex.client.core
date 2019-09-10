using Atomix.Core;

namespace Atomix.Api.Proto
{
    public class AuthOkScheme : ProtoScheme<AuthOk>
    {
        public AuthOkScheme(byte messageId)
            : base(messageId)
        {
            Model.Add(typeof(AuthOk), true);
        }
    }
}