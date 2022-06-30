using Atomex.Client.V1.Entities;

namespace Atomex.Client.V1.Proto
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