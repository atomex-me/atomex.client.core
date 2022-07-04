namespace Atomex.Client.V1.Entities
{
    public class Request<T>
    {
        public string Id { get; set; }
        public T Data { get; set; }
    }
}