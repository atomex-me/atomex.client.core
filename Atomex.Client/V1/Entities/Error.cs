namespace Atomex.Client.V1.Entities
{
    public class Error
    {
        public int Code { get; set; }
        public string Description { get; set; }
        public string Details { get; set; }
        public string RequestId { get; set; }
        public string OrderId { get; set; }
        public long? SwapId { get; set; }
    }
}