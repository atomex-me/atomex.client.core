using Atomex.Common;

namespace Atomex.Client.Common
{
    public class ServiceErrorEventArgs : ErrorEventArgs
    {
        public Service Service { get; }

        public ServiceErrorEventArgs(Error error, Service service)
            : base(error)
        {
            Service = service;
        }
    }
}