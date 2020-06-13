namespace Atomex.Api.Proto
{
    public class HeartBeatScheme : ProtoScheme<string>
    {
        public HeartBeatScheme(byte messageId)
            : base(messageId)
        {
        }
    }
}