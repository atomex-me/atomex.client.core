namespace Atomex.Services
{
    public class AtomexClientServiceEventArgs
    {
        public AtomexClientService Service { get; }

        public AtomexClientServiceEventArgs(AtomexClientService service)
        {
            Service = service;
        }
    }
}