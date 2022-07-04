namespace Atomex.Client.Common
{
    public class ServiceEventArgs
    {
        public Service Service { get; }
        public ServiceStatus Status { get; }

        public ServiceEventArgs(Service service, ServiceStatus status)
        {
            Service = service;
            Status = status;
        }
    }
}