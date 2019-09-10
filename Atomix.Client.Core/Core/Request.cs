namespace Atomix.Core
{
    public class Request<T>
    {
        public string Id { get; set; }
        public T Data { get; set; }
    }
}