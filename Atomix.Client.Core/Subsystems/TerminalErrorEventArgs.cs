namespace Atomix.Subsystems
{
    public class TerminalErrorEventArgs : TerminalServiceEventArgs
    {
        public string Description { get; }

        public TerminalErrorEventArgs(TerminalService service, string description)
            : base(service)
        {
            Description = description;
        }
    }
}