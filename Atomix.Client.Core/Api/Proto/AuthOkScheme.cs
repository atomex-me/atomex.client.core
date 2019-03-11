namespace Atomix.Api.Proto
{
    public class AuthOkScheme : ProtoScheme
    {
        public const int MessageId = 2;

        public AuthOkScheme()
            : base(MessageId)
        {
        }
    }
}