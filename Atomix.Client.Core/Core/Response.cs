namespace Atomix.Core
{
    public class Response<T>
    {
        public string RequestId { get; set; }
        public T Data { get; set; }
        public bool EndOfMessage { get; set; }
    }
}