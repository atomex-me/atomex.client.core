namespace Atomex.Client.V1.Entities
{
    public class Response<T>
    {
        public string RequestId { get; set; }
        public T Data { get; set; }
        public bool EndOfMessage { get; set; }
    }
}