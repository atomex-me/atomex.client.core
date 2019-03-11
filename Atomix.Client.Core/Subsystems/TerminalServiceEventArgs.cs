namespace Atomix.Subsystems
{
    public class TerminalServiceEventArgs
    {
        public TerminalService Service { get; }

        public TerminalServiceEventArgs(TerminalService service)
        {
            Service = service;
        }
    }
}