namespace Atomex.Client.V1.Proto
{
    public class HeartBeatScheme : ProtoScheme<string>
    {
        public HeartBeatScheme(byte messageId)
            : base(messageId)
        {
        }
    }
}